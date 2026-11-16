using Flowamaz.Core.Workflow;
using System.Text.Json;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Runtime;
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
    // Runtime hook: the product writes gate decisions to the audit log (ProductRunEventSink).
    private readonly IRunEventSink _runEvents;
    private readonly IWorkflowInstanceRepository? _instances;
    // Optional (null in unit tests): names the workflow in the gate-decided event (audit label).
    private readonly IWorkflowDefinitionRepository? _definitions;
    // Optional (null in unit tests): reads the run's version so gate lists show the gate's name.
    private readonly IWorkflowVersionRepository? _versions;

    public GateService(
        IGateDecisionRepository gates,
        IWorkflowEventRepository events,
        IWorkflowOrchestrator orchestrator,
        IUnitOfWork unitOfWork,
        ILogger<GateService> logger,
        IRunEventSink? runEvents = null,
        IWorkflowInstanceRepository? instances = null,
        IWorkflowDefinitionRepository? definitions = null,
        IWorkflowVersionRepository? versions = null)
    {
        _versions = versions;
        _instances = instances;
        _definitions = definitions;
        _gates = gates;
        _events = events;
        _orchestrator = orchestrator;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _runEvents = runEvents ?? NullRunEventSink.Instance;
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
            var result = await WithLabelsAsync(workspaceId,
                gates.Select(g => ToResponse(g) with { IsTest = testIds.Contains(g.InstanceId) }).ToList(), ct);
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
            var result = await WithLabelsAsync(workspaceId,
                gates.Select(g => ToResponse(g) with { IsTest = testIds.Contains(g.InstanceId) }).ToList(), ct);
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
        return (await WithLabelsAsync(workspaceId, [ToResponse(gate) with { IsTest = testIds.Contains(instanceId) }], ct))[0];
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
        CancellationToken ct = default, string? decidedByLabel = null, IReadOnlyDictionary<string, JsonElement>? values = null)
    {
        _logger.LogInformation(
            "GateService.DecideAsync enter workspace={WorkspaceId} instance={InstanceId} node={NodeId} decision={Decision}",
            workspaceId, instanceId, nodeId, decision);

        // Under the run's row lock (like the orchestrator), so a cancel landing at the same moment either ends the
        // run first (and this decision is refused below) or waits until the decision has been applied.
        await using var transaction = _unitOfWork.HasActiveTransaction ? null : await _unitOfWork.BeginTransactionAsync(ct);
        if (_instances is not null) await _instances.LockForUpdateAsync(instanceId, ct);

        var gate = await _gates.GetByNodeAsync(instanceId, nodeId, ct);
        if (gate is null || gate.WorkspaceId != workspaceId) return null;
        if (gate.Decision != GateDecisionStatus.Pending) throw new GateAlreadyDecidedException(nodeId);
        if (await _gates.IsInstanceFinishedAsync(instanceId, ct)) throw new GateRunEndedException(nodeId);

        // Strict: a typo must not silently count as a rejection.
        var approved = decision?.Trim().ToLowerInvariant() switch
        {
            "approved" or "approve" => true,
            "rejected" or "reject" => false,
            _ => throw new GateDecisionValueException(decision),
        };

        // A gate with a form takes its answers on approval; a rejection needs none (the note says why).
        var form = FormOf(gate);
        string? formValuesJson = null;
        if (approved && form.Count > 0)
        {
            var (checkedValues, problems) = GateForms.Check(form, values);
            if (problems.Count > 0) throw new GateFormException(problems);
            formValuesJson = JsonSerializer.Serialize(checkedValues);
            gate.FormValuesJson = formValuesJson;
        }

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
            // The answers travel on as the gate's output: {{ gate-id.output.form.<key> }}.
            var output = formValuesJson is null ? $$"""{"decision":"approved"}""" : $$"""{"decision":"approved","form":{{formValuesJson}}}""";
            await _orchestrator.CompleteNodeAsync(instanceId, nodeId, output, "gate", ct);
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

        if (transaction is not null) await transaction.CommitAsync(ct);

        await _runEvents.GateDecidedAsync(new GateDecidedEvent(
            workspaceId, gate.Id, instanceId, nodeId, approved, decider, gate.DecidedByLabel,
            await WorkflowNameAsync(workspaceId, instanceId, ct)), ct);

        _logger.LogInformation("GateService.DecideAsync exit instance={InstanceId} node={NodeId} approved={Approved}", instanceId, nodeId, approved);
        // The inbox replaces its row with this response, so it carries the gate's name like the list does.
        return (await WithLabelsAsync(workspaceId, [ToResponse(gate)], ct))[0];
    }

    private static GateResponse ToResponse(GateDecision g) => new(
        g.Id, g.InstanceId, g.NodeId, g.Decision.ToString(), g.AssignedToEmail,
        g.DeliveryChannel.ToString(), g.DeliveryStatus.ToString(), g.ExpiresAt, g.CreatedAt, g.DecidedAt, g.DecisionNote,
        DecidedByLabel: g.DecidedByLabel,
        Form: FormOf(g),
        FormValues: g.FormValuesJson is null ? null : JsonDocument.Parse(g.FormValuesJson).RootElement.Clone());

    /// <summary>The form captured when the gate opened.</summary>
    public static IReadOnlyList<GateFormField> FormOf(GateDecision g)
    {
        if (string.IsNullOrEmpty(g.FormJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<GateFormField>>(g.FormJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>True when approving needs answers only a person can give in Flowamaz (email/Slack buttons can't).</summary>
    public static bool NeedsFormAnswers(GateDecision g) => FormOf(g).Any(f => f.Required);

    /// <summary>
    /// Adds each gate's name (config.gate_label, else the node label) from the version its run is pinned
    /// to. One read per run and per version; a version that can't be read leaves the names empty.
    /// </summary>
    private async Task<List<GateResponse>> WithLabelsAsync(Guid workspaceId, List<GateResponse> gates, CancellationToken ct)
    {
        if (_instances is null || _versions is null || gates.Count == 0) return gates;

        var parser = new SfgParser();
        var graphs = new Dictionary<Guid, WorkflowGraph?>();
        var versionOfRun = new Dictionary<Guid, Guid?>();
        var result = new List<GateResponse>(gates.Count);
        foreach (var gate in gates)
        {
            if (!versionOfRun.TryGetValue(gate.InstanceId, out var versionId))
            {
                versionId = (await _instances.GetByIdForWorkspaceAsync(gate.InstanceId, workspaceId, ct))?.WorkflowVersionId;
                versionOfRun[gate.InstanceId] = versionId;
            }
            WorkflowGraph? graph = null;
            if (versionId is { } vid && !graphs.TryGetValue(vid, out graph))
            {
                try
                {
                    var version = await _versions.GetByIdForWorkspaceAsync(vid, workspaceId, ct);
                    graph = version is null ? null : parser.Parse(version.YamlContent);
                }
                catch (SfgParseException ex)
                {
                    _logger.LogWarning(ex, "GateService.WithLabelsAsync version={VersionId} doesn't parse; gate names left empty", vid);
                }
                graphs[vid] = graph;
            }

            var node = graph?.FindNode(gate.NodeId);
            if (node is null)
            {
                result.Add(gate);
                continue;
            }
            var label = string.IsNullOrWhiteSpace(node.Label) || node.Label == node.Id ? null : node.Label;
            var gateLabel = node.Config.RootElement.ValueKind == JsonValueKind.Object
                && node.Config.RootElement.TryGetProperty("gate_label", out var gl) && gl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(gl.GetString())
                    ? gl.GetString()
                    : null;
            result.Add(gate with { GateLabel = gateLabel, Label = label });
        }
        return result;
    }

    private async Task<string?> WorkflowNameAsync(Guid workspaceId, Guid instanceId, CancellationToken ct)
    {
        if (_instances is null || _definitions is null) return null;
        var instance = await _instances.GetByIdForWorkspaceAsync(instanceId, workspaceId, ct);
        if (instance is null) return null;
        var definition = await _definitions.GetByIdForWorkspaceAsync(instance.WorkflowDefinitionId, workspaceId, ct);
        return definition?.Name;
    }
}
