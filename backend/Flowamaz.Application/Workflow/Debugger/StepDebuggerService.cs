using System.Text.Json;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Debugger;

/// <summary>
/// Step debugger for Dev/Staging runs (FUNCTIONAL.md §8.10). Every entry point loads the instance
/// and refuses (403) if it is a Production run — the environment gate lives here, not just in the
/// controller. Pause/step/branch state lives in Redis (1h TTL) via <see cref="IDebugStateStore"/>.
/// </summary>
public sealed class StepDebuggerService : IStepDebuggerService
{
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowNodeStateRepository _nodeStates;
    private readonly IWorkflowVariableRepository _variables;
    private readonly IWorkflowEventRepository _events;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IDebugStateStore _debugState;
    private readonly ITaskQueue _queue;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SfgParser _parser;
    private readonly ILogger<StepDebuggerService> _logger;

    public StepDebuggerService(
        IWorkflowInstanceRepository instances,
        IWorkflowNodeStateRepository nodeStates,
        IWorkflowVariableRepository variables,
        IWorkflowEventRepository events,
        IWorkflowVersionRepository versions,
        IDebugStateStore debugState,
        ITaskQueue queue,
        IUnitOfWork unitOfWork,
        SfgParser parser,
        ILogger<StepDebuggerService> logger)
    {
        _instances = instances;
        _nodeStates = nodeStates;
        _variables = variables;
        _events = events;
        _versions = versions;
        _debugState = debugState;
        _queue = queue;
        _unitOfWork = unitOfWork;
        _parser = parser;
        _logger = logger;
    }

    public async Task<bool> PauseAsync(Guid workspaceId, Guid instanceId, string afterNodeId, CancellationToken ct = default)
    {
        var instance = await LoadGuardedAsync(workspaceId, instanceId, ct);
        if (instance is null) return false;
        await _debugState.SetPauseAfterAsync(instanceId, afterNodeId, ct);
        _logger.LogInformation("StepDebuggerService.PauseAsync instance={InstanceId} pauseAfter={NodeId}", instanceId, afterNodeId);
        return true;
    }

    public async Task<DebugSnapshot?> InspectAsync(Guid workspaceId, Guid instanceId, CancellationToken ct = default)
    {
        var instance = await LoadGuardedAsync(workspaceId, instanceId, ct);
        if (instance is null) return null;

        var states = await _nodeStates.GetForInstanceAsync(instanceId, ct);
        var variables = await _variables.GetForInstanceAsync(instanceId, ct);
        var events = await _events.GetForInstanceAsync(instanceId, ct);

        var vars = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal); // UNMASKED — dev/staging only
        var recent = events.TakeLast(20)
            .Select(e => $"{e.SequenceNumber}:{e.EventType}{(string.IsNullOrEmpty(e.NodeId) ? "" : $":{e.NodeId}")}")
            .ToList();

        return new DebugSnapshot(
            instanceId,
            instance.Status.ToString(),
            instance.CurrentNodeId,
            states.Select(s => new DebugNode(s.NodeId, s.NodeType, s.Status.ToString(), s.RetryCount)).ToList(),
            vars,
            recent,
            await NextEligibleNodesAsync(instance, states, ct));
    }

    public async Task<bool> ForceVariableAsync(Guid workspaceId, Guid instanceId, string name, string value, CancellationToken ct = default)
    {
        var instance = await LoadGuardedAsync(workspaceId, instanceId, ct);
        if (instance is null) return false;

        var existing = await _variables.GetByNameAsync(instanceId, name, ct);
        if (existing is null)
        {
            await _variables.AddAsync(new WorkflowVariable
            {
                WorkspaceId = workspaceId, InstanceId = instanceId, Name = name, Value = value,
            }, ct);
        }
        else
        {
            existing.Value = value;
            _variables.Update(existing);
        }

        var seq = await _events.GetNextSequenceNumberAsync(instanceId, ct);
        await _events.AppendAsync(new WorkflowEvent
        {
            WorkspaceId = workspaceId,
            InstanceId = instanceId,
            SequenceNumber = seq,
            EventType = "VariableForced",
            Payload = JsonSerializer.Serialize(new { name }),
            OccurredAt = DateTime.UtcNow,
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("StepDebuggerService.ForceVariableAsync instance={InstanceId} variable={Name}", instanceId, name);
        return true;
    }

    public async Task<bool> StepForwardAsync(Guid workspaceId, Guid instanceId, CancellationToken ct = default)
    {
        var instance = await LoadGuardedAsync(workspaceId, instanceId, ct);
        if (instance is null) return false;
        await _debugState.GrantStepAsync(instanceId, ct);
        await _queue.EnqueueAsync(workspaceId, instanceId, ct);
        return true;
    }

    public async Task<bool> ForceBranchAsync(Guid workspaceId, Guid instanceId, string routerNodeId, string targetBranchEdgeId, CancellationToken ct = default)
    {
        var instance = await LoadGuardedAsync(workspaceId, instanceId, ct);
        if (instance is null) return false;
        await _debugState.SetForcedBranchAsync(instanceId, routerNodeId, targetBranchEdgeId, ct);
        return true;
    }

    public async Task<bool> ResumeAsync(Guid workspaceId, Guid instanceId, CancellationToken ct = default)
    {
        var instance = await LoadGuardedAsync(workspaceId, instanceId, ct);
        if (instance is null) return false;
        await _debugState.ClearAsync(instanceId, ct);
        await _queue.EnqueueAsync(workspaceId, instanceId, ct);
        return true;
    }

    private async Task<WorkflowInstance?> LoadGuardedAsync(Guid workspaceId, Guid instanceId, CancellationToken ct)
    {
        var instance = await _instances.GetByIdForWorkspaceAsync(instanceId, workspaceId, ct);
        if (instance is null) return null;
        if (instance.EnvironmentType == WorkspaceEnvironmentType.Production)
        {
            throw new DebuggerNotAllowedException();
        }
        return instance;
    }

    private async Task<IReadOnlyList<string>> NextEligibleNodesAsync(WorkflowInstance instance, List<WorkflowNodeState> states, CancellationToken ct)
    {
        var version = await _versions.GetByIdForWorkspaceAsync(instance.WorkflowVersionId, instance.WorkspaceId, ct);
        if (version is null) return [];
        try
        {
            var graph = await _parser.ParseAsync(version.YamlContent, ct);
            var stated = states.Select(s => s.NodeId).ToHashSet(StringComparer.Ordinal);
            var completed = states.Where(s => s.Status == NodeStatus.Completed).Select(s => s.NodeId).ToHashSet(StringComparer.Ordinal);
            // The trigger is implicitly complete once the run has started.
            completed.Add(graph.TriggerNode.Id);

            return completed
                .SelectMany(graph.OutgoingEdges)
                .Select(e => e.ToNodeId)
                .Where(n => !stated.Contains(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (SfgParseException)
        {
            return [];
        }
    }
}
