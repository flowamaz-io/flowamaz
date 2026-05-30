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

    public async Task<List<GateResponse>> ListPendingAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var gates = await _gates.GetPendingForWorkspaceAsync(workspaceId, ct);
        return gates.Select(ToResponse).ToList();
    }

    public async Task<GateResponse?> GetAsync(Guid workspaceId, Guid instanceId, string nodeId, CancellationToken ct = default)
    {
        var gate = await _gates.GetByNodeAsync(instanceId, nodeId, ct);
        return gate is null || gate.WorkspaceId != workspaceId ? null : ToResponse(gate);
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

    public async Task<GateResponse?> DecideAsync(
        Guid workspaceId, Guid instanceId, string nodeId, string decision, string? note, Guid decidedBy, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "GateService.DecideAsync enter workspace={WorkspaceId} instance={InstanceId} node={NodeId} decision={Decision}",
            workspaceId, instanceId, nodeId, decision);

        var gate = await _gates.GetByNodeAsync(instanceId, nodeId, ct);
        if (gate is null || gate.WorkspaceId != workspaceId) return null;
        if (gate.Decision != GateDecisionStatus.Pending) throw new GateAlreadyDecidedException(nodeId);

        var approved = string.Equals(decision, "approved", StringComparison.OrdinalIgnoreCase);
        gate.Decision = approved ? GateDecisionStatus.Approved : GateDecisionStatus.Rejected;
        gate.DecidedBy = decidedBy;
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
            Payload = $$"""{"decision":"{{gate.Decision}}"}""",
            OccurredAt = DateTime.UtcNow,
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        if (approved)
        {
            await _orchestrator.CompleteNodeAsync(instanceId, nodeId, $$"""{"decision":"approved"}""", "gate", ct);
        }
        else
        {
            await _orchestrator.FailNodeAsync(instanceId, nodeId, $"Gate rejected{(string.IsNullOrWhiteSpace(note) ? "" : $": {note}")}", "gate", ct);
        }

        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            WorkspaceId = workspaceId,
            ActorUserId = decidedBy,
            ActorType = "user",
            EventType = approved ? "gate.approved" : "gate.rejected",
            ResourceType = "gate",
            ResourceId = gate.Id,
            ResourceLabel = nodeId,
            Action = approved ? "approved" : "rejected",
            Metadata = new { instanceId, nodeId },
        }, ct);

        _logger.LogInformation("GateService.DecideAsync exit instance={InstanceId} node={NodeId} approved={Approved}", instanceId, nodeId, approved);
        return ToResponse(gate);
    }

    private static GateResponse ToResponse(GateDecision g) => new(
        g.Id, g.InstanceId, g.NodeId, g.Decision.ToString(), g.AssignedToEmail,
        g.DeliveryChannel.ToString(), g.DeliveryStatus.ToString(), g.ExpiresAt, g.CreatedAt, g.DecidedAt, g.DecisionNote);
}
