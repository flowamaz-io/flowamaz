using System.Text.Json;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Services;

/// <summary>
/// Human-approval gate queries + decisions. Approving resumes the instance (the gate node is
/// completed and the instance re-queued); rejecting fails the gate node, which triggers the
/// instance's failure/compensation path. Records a GateDecided event either way.
/// </summary>
public sealed class GateService
{
    private readonly IGateDecisionRepository _gates;
    private readonly IWorkflowEventRepository _events;
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GateService> _logger;
    private readonly IAuditService? _audit;

    public GateService(
        IGateDecisionRepository gates,
        IWorkflowEventRepository events,
        IWorkflowOrchestrator orchestrator,
        IUnitOfWork unitOfWork,
        ILogger<GateService> logger,
        IAuditService? audit = null)
    {
        _gates = gates;
        _events = events;
        _orchestrator = orchestrator;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _audit = audit;
    }

    /// <summary>
    /// Pending gates for the approval queue. Test-run gates are excluded by default (O2) and, when
    /// <paramref name="includeTest"/> is set, returned with <c>IsTest = true</c> so the UI can tag them.
    /// </summary>
    public async Task<List<GateResponse>> ListPendingAsync(Guid workspaceId, bool includeTest = false, CancellationToken ct = default)
    {
        _logger.LogDebug("GateService.ListPendingAsync entry workspace={WorkspaceId} includeTest={IncludeTest}", workspaceId, includeTest);
        try
        {
            var gates = await _gates.GetPendingForWorkspaceAsync(workspaceId, includeTest, ct);
            var testIds = includeTest && gates.Count > 0
                ? await _gates.GetTestInstanceIdsAsync(workspaceId, gates.Select(g => g.InstanceId).Distinct().ToList(), ct) ?? []
                : [];
            var result = gates.Select(g => ToResponse(g) with { IsTest = testIds.Contains(g.InstanceId) }).ToList();
            _logger.LogDebug("GateService.ListPendingAsync exit workspace={WorkspaceId} count={Count}", workspaceId, result.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "GateService.ListPendingAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }

    /// <summary>Gates for the "All" tab: decided ones plus actionable pending ones, newest first (max 200).</summary>
    public async Task<List<GateResponse>> ListRecentAsync(Guid workspaceId, bool includeTest = false, CancellationToken ct = default)
    {
        _logger.LogDebug("GateService.ListRecentAsync entry workspace={WorkspaceId} includeTest={IncludeTest}", workspaceId, includeTest);
        try
        {
            var gates = await _gates.GetRecentForWorkspaceAsync(workspaceId, includeTest, 200, ct);
            var testIds = includeTest && gates.Count > 0
                ? await _gates.GetTestInstanceIdsAsync(workspaceId, gates.Select(g => g.InstanceId).Distinct().ToList(), ct) ?? []
                : [];
            var result = gates.Select(g => ToResponse(g) with { IsTest = testIds.Contains(g.InstanceId) }).ToList();
            _logger.LogDebug("GateService.ListRecentAsync exit workspace={WorkspaceId} count={Count}", workspaceId, result.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "GateService.ListRecentAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task<GateResponse?> GetAsync(Guid workspaceId, Guid instanceId, string nodeId, CancellationToken ct = default)
    {
        var gate = await _gates.GetByNodeAsync(instanceId, nodeId, ct);
        if (gate is null || gate.WorkspaceId != workspaceId) return null;
        var testIds = await _gates.GetTestInstanceIdsAsync(workspaceId, [instanceId], ct) ?? [];
        return ToResponse(gate) with { IsTest = testIds.Contains(instanceId) };
    }

    /// <summary>
    /// Returns minimal gate metadata by gate ID (no workspace scoping — used only for signed
    /// email/Slack link flows where the workspace is unknown until the gate is loaded).
    /// Callers must verify the HMAC signature before acting on the returned entity.
    /// </summary>
    public async Task<GateInfoDto?> GetByGateIdAsync(Guid gateId, CancellationToken ct = default)
    {
        var gate = await _gates.GetByIdAsync(gateId, ct);
        if (gate is null) return null;

        return new GateInfoDto(gate.Id, gate.WorkspaceId, gate.InstanceId, gate.NodeId, gate.ExpiresAt);
    }

    /// <summary>
    /// Records an approve/reject decision and resumes the run. <paramref name="decidedBy"/> is the Flowamaz
    /// user, or <see cref="Guid.Empty"/> for public-API and signed-link decisions. <paramref name="decidedByLabel"/>
    /// is the decider as named by an external application through the public API (<c>decided_by</c>); it is
    /// stored on the gate, carried on the GateDecided event and shown in the audit log next to the API key.
    /// </summary>
    public async Task<GateResponse?> DecideAsync(
        Guid workspaceId, Guid instanceId, string nodeId, string decision, string? note, Guid decidedBy,
        CancellationToken ct = default, string? decidedByLabel = null)
    {
        _logger.LogInformation(
            "GateService.DecideAsync enter workspace={WorkspaceId} instance={InstanceId} node={NodeId} decision={Decision}",
            workspaceId, instanceId, nodeId, decision);

        var gate = await _gates.GetByNodeAsync(instanceId, nodeId, ct);
        if (gate is null || gate.WorkspaceId != workspaceId) return null;
        if (gate.Decision != GateDecisionStatus.Pending) throw new GateAlreadyDecidedException(nodeId);
        if (await _gates.IsInstanceFinishedAsync(instanceId, ct)) throw new GateRunEndedException(nodeId);

        var approved = string.Equals(decision, "approved", StringComparison.OrdinalIgnoreCase);
        gate.Decision = approved ? GateDecisionStatus.Approved : GateDecisionStatus.Rejected;
        // Public-API decisions pass Guid.Empty (no human); the audit row is attributed to the API key (O4).
        Guid? decider = decidedBy == Guid.Empty ? null : decidedBy;
        gate.DecidedBy = decider;
        gate.DecidedByLabel = string.IsNullOrWhiteSpace(decidedByLabel) ? null : decidedByLabel.Trim();
        gate.DecidedAt = DateTime.UtcNow;
        gate.DecisionNote = note;
        _gates.Update(gate);

        var seq = await _events.GetNextSequenceNumberAsync(instanceId, ct);
        await _events.AppendAsync(new WorkflowEvent
        {
            WorkspaceId = workspaceId,
            InstanceId = instanceId,
            SequenceNumber = seq,
            EventType = "GateDecided",
            NodeId = nodeId,
            NodeType = nameof(NodeType.HumanGate),
            // Serialized (not interpolated): decided_by is caller-supplied text.
            Payload = JsonSerializer.Serialize(gate.DecidedByLabel is null
                ? new Dictionary<string, string> { ["decision"] = gate.Decision.ToString() }
                : new Dictionary<string, string> { ["decision"] = gate.Decision.ToString(), ["decided_by"] = gate.DecidedByLabel }),
            OccurredAt = DateTime.UtcNow,
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        if (approved)
        {
            await _orchestrator.CompleteNodeAsync(instanceId, nodeId, $$"""{"decision":"approved"}""", "gate", ct);
        }
        else if (await _orchestrator.HasDecisionRouteAsync(instanceId, nodeId, "rejected", ct))
        {
            // The workflow handles rejection (e.g. "Approved?" router → notify requester): continue down that path.
            await _orchestrator.CompleteNodeAsync(instanceId, nodeId, $$"""{"decision":"rejected"}""", "gate", ct);
        }
        else
        {
            await _orchestrator.FailNodeAsync(instanceId, nodeId, $"Gate rejected{(string.IsNullOrWhiteSpace(note) ? "" : $": {note}")}", "gate", ct,
                retryable: false);
        }

        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            WorkspaceId = workspaceId,
            ActorUserId = decider,
            ActorType = decider.HasValue ? "user" : "system",
            // Named decider from the calling application; AuditService adds the API key ("… via API key …").
            ActorLabel = decider.HasValue ? null : gate.DecidedByLabel,
            EventType = approved ? "gate.approved" : "gate.rejected",
            ResourceType = "gate",
            ResourceId = gate.Id,
            ResourceLabel = nodeId,
            Action = approved ? "approved" : "rejected",
            Metadata = gate.DecidedByLabel is null
                ? new { instanceId, nodeId }
                : (object)new { instanceId, nodeId, decidedBy = gate.DecidedByLabel },
        }, ct);

        _logger.LogInformation("GateService.DecideAsync exit instance={InstanceId} node={NodeId} approved={Approved}", instanceId, nodeId, approved);
        return ToResponse(gate);
    }

    private static GateResponse ToResponse(GateDecision g) => new(
        g.Id, g.InstanceId, g.NodeId, g.Decision.ToString(), g.AssignedToEmail,
        g.DeliveryChannel.ToString(), g.DeliveryStatus.ToString(), g.ExpiresAt, g.CreatedAt, g.DecidedAt, g.DecisionNote,
        DecidedByLabel: g.DecidedByLabel);
}
