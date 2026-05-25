using System.Text.Json;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Saga;

/// <summary>
/// Saga compensation engine (prompt 02-03, FUNCTIONAL.md §2.7). Backward undoes completed nodes in
/// reverse execution order, Forward retries the failed node, Pivot compensates before the failure
/// then retries forward. Compensation step failures are logged and skipped — they never throw, so a
/// half-failed compensation still records a CompensationFailed outcome rather than crashing.
/// </summary>
public sealed class SagaEngine : ISagaEngine
{
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowNodeStateRepository _nodeStates;
    private readonly IWorkflowVariableRepository _variables;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowEventRepository _events;
    private readonly INodeWorkerRegistry _workers;
    private readonly ITaskQueue _queue;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SfgParser _parser;
    private readonly ILogger<SagaEngine> _logger;

    public SagaEngine(
        IWorkflowInstanceRepository instances,
        IWorkflowNodeStateRepository nodeStates,
        IWorkflowVariableRepository variables,
        IWorkflowVersionRepository versions,
        IWorkflowEventRepository events,
        INodeWorkerRegistry workers,
        ITaskQueue queue,
        IUnitOfWork unitOfWork,
        SfgParser parser,
        ILogger<SagaEngine> logger)
    {
        _instances = instances;
        _nodeStates = nodeStates;
        _variables = variables;
        _versions = versions;
        _events = events;
        _workers = workers;
        _queue = queue;
        _unitOfWork = unitOfWork;
        _parser = parser;
        _logger = logger;
    }

    public async Task StartAsync(Guid instanceId, string failedNodeId, SagaStrategyType strategy, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "SagaEngine.StartAsync enter instance={InstanceId} failedNode={FailedNode} strategy={Strategy}",
            instanceId, failedNodeId, strategy);

        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' not found starting saga.");

        var version = await _versions.GetByIdForWorkspaceAsync(instance.WorkflowVersionId, instance.WorkspaceId, cancellationToken)
            ?? throw new InvalidOperationException($"Pinned version missing for instance '{instanceId}'.");
        var graph = await _parser.ParseAsync(version.YamlContent, cancellationToken);
        var states = await _nodeStates.GetForInstanceAsync(instanceId, cancellationToken);

        var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);
        instance.SagaState = SagaState.Compensating;
        instance.SagaStrategy = strategy.ToString();
        _instances.Update(instance);
        seq = await AppendEventAsync(seq, instance, "CompensationStarted", failedNodeId, null, $$"""{"strategy":"{{strategy}}"}""", cancellationToken);

        switch (strategy)
        {
            case SagaStrategyType.Forward:
                seq = await ForwardAsync(instance, failedNodeId, states, seq, cancellationToken);
                break;
            case SagaStrategyType.Pivot:
                seq = await PivotAsync(instance, graph, failedNodeId, states, seq, cancellationToken);
                break;
            default:
                seq = await BackwardAsync(instance, graph, states, seq, cancellationToken);
                break;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("SagaEngine.StartAsync exit instance={InstanceId} sagaState={SagaState} status={Status}",
            instanceId, instance.SagaState, instance.Status);
    }

    private async Task<long> BackwardAsync(WorkflowInstance instance, WorkflowGraph graph, List<WorkflowNodeState> states, long seq, CancellationToken ct)
    {
        var reverseCompleted = states
            .Where(s => s.Status == NodeStatus.Completed)
            .OrderBy(s => s.CompletedAt ?? s.StartedAt ?? DateTime.MaxValue)
            .Reverse()
            .ToList();

        var anyFailed = false;
        var variables = await LoadVariablesAsync(instance.Id, ct);

        foreach (var state in reverseCompleted)
        {
            var node = graph.FindNode(state.NodeId);
            var compensateNodeId = node?.Compensation?.CompensateNodeId;
            if (compensateNodeId is null) continue;

            var compensateNode = graph.FindNode(compensateNodeId);
            if (compensateNode is null)
            {
                _logger.LogWarning("SagaEngine compensate node '{CompensateNodeId}' missing for '{NodeId}'", compensateNodeId, state.NodeId);
                continue;
            }

            var worker = _workers.Resolve(compensateNode.Type);
            if (worker is null)
            {
                _logger.LogWarning("SagaEngine no worker for compensate node type {Type}", compensateNode.Type);
                continue;
            }

            try
            {
                var context = new NodeExecutionContext(instance.Id, instance.WorkspaceId, compensateNode, graph, variables, "saga");
                var result = await worker.ExecuteAsync(context, ct);
                seq = await AppendEventAsync(seq, instance, "NodeCompleted", compensateNode.Id, compensateNode.Type.ToString(),
                    $$"""{"compensatedNode":"{{state.NodeId}}","success":{{(result.Success ? "true" : "false")}}}""", ct);
                if (!result.Success)
                {
                    anyFailed = true;
                    _logger.LogWarning("SagaEngine compensation step failed for '{NodeId}': {Error}", state.NodeId, result.ErrorMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                anyFailed = true;
                _logger.LogError(ex, "SagaEngine compensation step threw for '{NodeId}' — continuing", state.NodeId);
            }
        }

        return await FinalizeAsync(instance, seq, anyFailed, ct);
    }

    private async Task<long> ForwardAsync(WorkflowInstance instance, string failedNodeId, List<WorkflowNodeState> states, long seq, CancellationToken ct)
    {
        var state = states.FirstOrDefault(s => s.NodeId == failedNodeId);
        if (state is not null)
        {
            state.Status = NodeStatus.Pending;
            state.RetryCount = 0;
            state.ErrorMessage = null;
            _nodeStates.Update(state);
        }

        instance.SagaState = SagaState.None;
        if (instance.CanTransitionTo(InstanceStatus.Running)) instance.TransitionTo(InstanceStatus.Running);
        _instances.Update(instance);

        await _queue.EnqueueAsync(instance.WorkspaceId, instance.Id, ct);
        return await AppendEventAsync(seq, instance, "CompensationCompleted", failedNodeId, null, """{"strategy":"Forward"}""", ct);
    }

    private async Task<long> PivotAsync(WorkflowInstance instance, WorkflowGraph graph, string failedNodeId, List<WorkflowNodeState> states, long seq, CancellationToken ct)
    {
        // Compensate everything completed before the pivot, then retry the pivot forward.
        var before = states
            .Where(s => s.Status == NodeStatus.Completed && s.NodeId != failedNodeId)
            .ToList();
        seq = await BackwardAsync(instance, graph, before, seq, ct);

        // Backward set a terminal state; Pivot resumes, so retry the failed node forward.
        return await ForwardAsync(instance, failedNodeId, states, seq, ct);
    }

    private async Task<long> FinalizeAsync(WorkflowInstance instance, long seq, bool anyFailed, CancellationToken ct)
    {
        if (anyFailed)
        {
            instance.SagaState = SagaState.CompensationFailed;
            if (instance.CanTransitionTo(InstanceStatus.Failed)) instance.TransitionTo(InstanceStatus.Failed);
            instance.FailedAt ??= DateTime.UtcNow;
            _instances.Update(instance);
            return await AppendEventAsync(seq, instance, "CompensationFailed", null, null, "{}", ct);
        }

        instance.SagaState = SagaState.None;
        if (instance.CanTransitionTo(InstanceStatus.Failed)) instance.TransitionTo(InstanceStatus.Failed);
        instance.FailedAt ??= DateTime.UtcNow;
        _instances.Update(instance);
        return await AppendEventAsync(seq, instance, "CompensationCompleted", null, null, "{}", ct);
    }

    private async Task<Dictionary<string, JsonElement>> LoadVariablesAsync(Guid instanceId, CancellationToken ct)
    {
        var rows = await _variables.GetForInstanceAsync(instanceId, ct);
        var dict = new Dictionary<string, JsonElement>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(row.Value) ? "null" : row.Value);
                dict[row.Name] = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(row.Value));
                dict[row.Name] = doc.RootElement.Clone();
            }
        }

        return dict;
    }

    private async Task<long> AppendEventAsync(
        long sequenceNumber, WorkflowInstance instance, string eventType, string? nodeId, string? nodeType, string payload, CancellationToken ct)
    {
        await _events.AppendAsync(new WorkflowEvent
        {
            WorkspaceId = instance.WorkspaceId,
            InstanceId = instance.Id,
            SequenceNumber = sequenceNumber,
            EventType = eventType,
            NodeId = nodeId,
            NodeType = nodeType,
            Payload = payload,
            OccurredAt = DateTime.UtcNow,
        }, ct);
        return sequenceNumber + 1;
    }
}
