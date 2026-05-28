using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Orchestrator;

/// <summary>
/// Durable execution core (prompt 02-02). Triggering pins a version, creates a Pending instance and
/// enqueues it; <see cref="StepAsync"/> walks the SFG one frontier at a time. Routing/Parallel nodes
/// resolve instantly within a step; Action/AI/Gate nodes are surfaced for the node executors that
/// arrive in prompt 02-03. Every transition is written to the append-only event log.
/// </summary>
public sealed class WorkflowOrchestrator : IWorkflowOrchestrator
{
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowNodeStateRepository _nodeStates;
    private readonly IWorkflowVariableRepository _variables;
    private readonly IWorkflowEventRepository _events;
    private readonly ITaskQueue _queue;
    private readonly IVariableEvaluationService _variableEvaluation;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SfgParser _parser;
    private readonly ILogger<WorkflowOrchestrator> _logger;
    // Optional so unit tests can construct the orchestrator without the saga engine; DI supplies it
    // at runtime. When present, a terminal failure with a compensation block runs the saga.
    private readonly ISagaEngine? _sagaEngine;

    public WorkflowOrchestrator(
        IWorkflowDefinitionRepository definitions,
        IWorkflowVersionRepository versions,
        IWorkflowInstanceRepository instances,
        IWorkflowNodeStateRepository nodeStates,
        IWorkflowVariableRepository variables,
        IWorkflowEventRepository events,
        ITaskQueue queue,
        IVariableEvaluationService variableEvaluation,
        IUnitOfWork unitOfWork,
        SfgParser parser,
        ILogger<WorkflowOrchestrator> logger,
        ISagaEngine? sagaEngine = null)
    {
        _definitions = definitions;
        _versions = versions;
        _instances = instances;
        _nodeStates = nodeStates;
        _variables = variables;
        _events = events;
        _queue = queue;
        _variableEvaluation = variableEvaluation;
        _unitOfWork = unitOfWork;
        _parser = parser;
        _logger = logger;
        _sagaEngine = sagaEngine;
    }

    public async Task<WorkflowInstance> TriggerAsync(
        Guid workspaceId,
        Guid workflowDefinitionId,
        string? payload,
        string? idempotencyKey,
        InstanceTriggerType triggerType = InstanceTriggerType.Manual,
        string? correlationId = null,
        bool isTest = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "WorkflowOrchestrator.TriggerAsync enter workspace={WorkspaceId} definition={DefinitionId} idempotencyKey={IdempotencyKey} isTest={IsTest}",
            workspaceId, workflowDefinitionId, idempotencyKey, isTest);

        try
        {
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existing = await _instances.GetByIdempotencyKeyAsync(workspaceId, idempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    _logger.LogInformation(
                        "WorkflowOrchestrator.TriggerAsync dedup — idempotencyKey={IdempotencyKey} returns existing instance={InstanceId}",
                        idempotencyKey, existing.Id);
                    return existing;
                }
            }

            var definition = await _definitions.GetByIdForWorkspaceAsync(workflowDefinitionId, workspaceId, cancellationToken)
                ?? throw new WorkflowNotFoundException(workflowDefinitionId);

            WorkflowVersion version;
            if (isTest)
            {
                // Test runs accept draft or published workflows — prefer the production version,
                // fall back to any saved version, then create a snapshot from the current YAML.
                version = await _versions.GetProductionAsync(definition.Id, workspaceId, cancellationToken)
                          ?? await _versions.GetLatestAsync(definition.Id, workspaceId, cancellationToken)
                          ?? await CreateDraftSnapshotAsync(definition, workspaceId, cancellationToken);
            }
            else
            {
                if (definition.Status != WorkflowStatus.Published)
                    throw new WorkflowNotPublishedException(workflowDefinitionId);
                version = await _versions.GetProductionAsync(definition.Id, workspaceId, cancellationToken)
                    ?? throw new WorkflowNotPublishedException(workflowDefinitionId);
            }

            var instance = new WorkflowInstance
            {
                WorkspaceId = workspaceId,
                WorkflowDefinitionId = definition.Id,
                WorkflowVersionId = version.Id,
                Status = InstanceStatus.Pending,
                TriggerType = triggerType,
                TriggerPayload = string.IsNullOrWhiteSpace(payload) ? null : payload,
                CorrelationId = correlationId,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
                IsTest = isTest,
                TestExpiresAt = isTest ? DateTime.UtcNow.AddHours(24) : null,
            };

            await _instances.AddAsync(instance, cancellationToken);
            await AppendEventAsync(1, instance, "InstanceStarted", null, null, instance.TriggerPayload ?? "{}", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _queue.EnqueueAsync(workspaceId, instance.Id, cancellationToken);

            _logger.LogInformation(
                "WorkflowOrchestrator.TriggerAsync exit instance={InstanceId} version={VersionId} isTest={IsTest}",
                instance.Id, version.Id, isTest);
            return instance;
        }
        catch (Exception ex) when (ex is not AppException and not OperationCanceledException)
        {
            _logger.LogError(ex,
                "WorkflowOrchestrator.TriggerAsync error workspace={WorkspaceId} definition={DefinitionId}",
                workspaceId, workflowDefinitionId);
            throw;
        }
    }

    private async Task<WorkflowVersion> CreateDraftSnapshotAsync(
        WorkflowDefinition definition, Guid workspaceId, CancellationToken ct)
    {
        var snapshot = new WorkflowVersion
        {
            WorkspaceId = workspaceId,
            WorkflowDefinitionId = definition.Id,
            YamlContent = definition.YamlContent,
            CommitSha = $"draft-{DateTime.UtcNow:yyyyMMddHHmmss}",
            BranchName = "draft",
            Message = "Test run snapshot",
            IsProduction = false,
        };
        await _versions.AddAsync(snapshot, ct);
        return snapshot;
    }

    public async Task<OrchestratorResult> StepAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WorkflowOrchestrator.StepAsync enter instance={InstanceId} lease={LeaseId}", instanceId, leaseId);

        try
        {
            var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
                ?? throw new InvalidOperationException($"Instance '{instanceId}' not found while stepping.");

            if (instance.Status is InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled)
            {
                return OrchestratorResult.Complete;
            }

            var graph = await LoadGraphAsync(instance, cancellationToken);
            var states = await _nodeStates.GetForInstanceAsync(instanceId, cancellationToken);
            var statedNodeIds = states.Select(s => s.NodeId).ToHashSet(StringComparer.Ordinal);
            var completedNodeIds = states.Where(s => s.Status == NodeStatus.Completed)
                .Select(s => s.NodeId).ToHashSet(StringComparer.Ordinal);

            var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);

            // First step: enter Running and treat the Trigger as completed (it is the entry marker).
            if (instance.Status == InstanceStatus.Pending)
            {
                instance.TransitionTo(InstanceStatus.Running);
                instance.StartedAt = DateTime.UtcNow;
                _instances.Update(instance);

                var trigger = graph.TriggerNode;
                await _nodeStates.AddAsync(NewState(instance, trigger, NodeStatus.Completed), cancellationToken);
                seq = await AppendEventAsync(seq, instance, "NodeCompleted", trigger.Id, trigger.Type.ToString(), "{}", cancellationToken);
                statedNodeIds.Add(trigger.Id);
                completedNodeIds.Add(trigger.Id);
            }

            var nextNodes = new List<string>();
            var needsGate = false;
            var reachedEnd = false;

            // Seed the work queue with successors of every completed node whose edge condition holds.
            var work = new Queue<string>();
            foreach (var source in completedNodeIds)
            {
                foreach (var edge in graph.OutgoingEdges(source))
                {
                    if (await _variableEvaluation.EvaluateConditionAsync(instanceId, edge.Condition, cancellationToken))
                    {
                        work.Enqueue(edge.ToNodeId);
                    }
                }
            }

            while (work.Count > 0)
            {
                var nodeId = work.Dequeue();
                if (!statedNodeIds.Add(nodeId)) continue; // already entered
                var node = graph.FindNode(nodeId);
                if (node is null) continue;

                switch (node.Type)
                {
                    case NodeType.End:
                        await _nodeStates.AddAsync(NewState(instance, node, NodeStatus.Completed), cancellationToken);
                        seq = await AppendEventAsync(seq, instance, "NodeCompleted", node.Id, node.Type.ToString(), "{}", cancellationToken);
                        reachedEnd = true;
                        break;

                    case NodeType.HumanGate:
                        await _nodeStates.AddAsync(NewState(instance, node, NodeStatus.Pending), cancellationToken);
                        seq = await AppendEventAsync(seq, instance, "GateOpened", node.Id, node.Type.ToString(), "{}", cancellationToken);
                        needsGate = true;
                        nextNodes.Add(node.Id);
                        break;

                    case NodeType.Router or NodeType.IfElse or NodeType.Switch:
                        // Routing is instantaneous: mark complete and follow matching edges.
                        await _nodeStates.AddAsync(NewState(instance, node, NodeStatus.Completed), cancellationToken);
                        seq = await AppendEventAsync(seq, instance, "NodeCompleted", node.Id, node.Type.ToString(), "{}", cancellationToken);
                        foreach (var edge in graph.OutgoingEdges(node.Id))
                        {
                            if (await _variableEvaluation.EvaluateConditionAsync(instanceId, edge.Condition, cancellationToken))
                            {
                                work.Enqueue(edge.ToNodeId);
                            }
                        }
                        break;

                    case NodeType.Parallel:
                        // Fan out: mark complete and enter every branch.
                        await _nodeStates.AddAsync(NewState(instance, node, NodeStatus.Completed), cancellationToken);
                        seq = await AppendEventAsync(seq, instance, "NodeCompleted", node.Id, node.Type.ToString(), "{}", cancellationToken);
                        foreach (var edge in graph.OutgoingEdges(node.Id))
                        {
                            work.Enqueue(edge.ToNodeId);
                        }
                        break;

                    default:
                        // Action/AI/Wait/ForEach/TryCatch/While/SubWorkflow — needs an external executor (02-03).
                        await _nodeStates.AddAsync(NewState(instance, node, NodeStatus.Pending), cancellationToken);
                        seq = await AppendEventAsync(seq, instance, "NodeStarted", node.Id, node.Type.ToString(), "{}", cancellationToken);
                        nextNodes.Add(node.Id);
                        break;
                }
            }

            var hasOutstanding = nextNodes.Count > 0
                || states.Any(s => s.Status is NodeStatus.Pending or NodeStatus.Running);
            var isComplete = reachedEnd && !hasOutstanding;

            if (isComplete)
            {
                instance.TransitionTo(InstanceStatus.Completed);
                instance.CompletedAt = DateTime.UtcNow;
                instance.CurrentNodeId = null;
                _instances.Update(instance);
                seq = await AppendEventAsync(seq, instance, "InstanceCompleted", null, null, "{}", cancellationToken);
            }
            else if (nextNodes.Count > 0)
            {
                instance.CurrentNodeId = nextNodes[0];
                _instances.Update(instance);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "WorkflowOrchestrator.StepAsync exit instance={InstanceId} next={Next} complete={Complete} gate={Gate}",
                instanceId, nextNodes.Count, isComplete, needsGate);

            if (isComplete) return OrchestratorResult.Complete;
            return needsGate ? OrchestratorResult.Gate(nextNodes) : OrchestratorResult.Continue(nextNodes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WorkflowOrchestrator.StepAsync error instance={InstanceId}", instanceId);
            throw;
        }
    }

    public async Task CompleteNodeAsync(Guid instanceId, string nodeId, string output, string leaseId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WorkflowOrchestrator.CompleteNodeAsync enter instance={InstanceId} node={NodeId}", instanceId, nodeId);

        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' not found completing node '{nodeId}'.");
        var state = await _nodeStates.GetByNodeAsync(instanceId, nodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Node state '{nodeId}' not found on instance '{instanceId}'.");

        state.Status = NodeStatus.Completed;
        state.CompletedAt = DateTime.UtcNow;
        state.OutputPayload = string.IsNullOrWhiteSpace(output) ? null : output;
        _nodeStates.Update(state);

        await UpsertVariableAsync(instance, $"{nodeId}.output", string.IsNullOrWhiteSpace(output) ? "null" : output, cancellationToken);

        var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);
        await AppendEventAsync(seq, instance, "NodeCompleted", nodeId, state.NodeType, state.OutputPayload ?? "{}", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // More steps remain — put the instance back on its queue for the next frontier.
        await _queue.EnqueueAsync(instance.WorkspaceId, instanceId, cancellationToken);
        _logger.LogInformation("WorkflowOrchestrator.CompleteNodeAsync exit instance={InstanceId} node={NodeId}", instanceId, nodeId);
    }

    public async Task FailNodeAsync(Guid instanceId, string nodeId, string error, string leaseId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WorkflowOrchestrator.FailNodeAsync enter instance={InstanceId} node={NodeId}", instanceId, nodeId);

        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' not found failing node '{nodeId}'.");
        var state = await _nodeStates.GetByNodeAsync(instanceId, nodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Node state '{nodeId}' not found on instance '{instanceId}'.");

        var graph = await LoadGraphAsync(instance, cancellationToken);
        var node = graph.FindNode(nodeId);
        var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);

        var retry = node?.RetryPolicy;
        if (retry is not null && state.RetryCount < retry.MaxAttempts)
        {
            state.RetryCount++;
            state.LastRetryAt = DateTime.UtcNow;
            state.Status = NodeStatus.Pending;
            state.ErrorMessage = error;
            _nodeStates.Update(state);
            seq = await AppendEventAsync(seq, instance, "NodeFailed", nodeId, state.NodeType, RetryPayload(error, state.RetryCount), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var delay = (int)Math.Round(retry.BackoffSeconds * Math.Pow(retry.BackoffMultiplier, state.RetryCount - 1));
            await _queue.EnqueueDelayedAsync(instance.WorkspaceId, instanceId, delay, cancellationToken);
            _logger.LogWarning(
                "WorkflowOrchestrator.FailNodeAsync retry instance={InstanceId} node={NodeId} attempt={Attempt} delay={Delay}s",
                instanceId, nodeId, state.RetryCount, delay);
            return;
        }

        state.Status = NodeStatus.Failed;
        state.ErrorMessage = error;
        _nodeStates.Update(state);
        seq = await AppendEventAsync(seq, instance, "NodeFailed", nodeId, state.NodeType, ErrorPayload(error), cancellationToken);

        var compensation = node?.Compensation;
        if (compensation is not null && _sagaEngine is not null)
        {
            // Persist the node failure, then hand off to the saga engine which drives the chosen
            // strategy and records CompensationStarted → CompensationCompleted/Failed itself.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var strategy = Enum.TryParse<SagaStrategyType>(compensation.Strategy, ignoreCase: true, out var parsed)
                ? parsed : SagaStrategyType.Backward;
            await _sagaEngine.StartAsync(instanceId, nodeId, strategy, cancellationToken);
            _logger.LogInformation("WorkflowOrchestrator.FailNodeAsync exit instance={InstanceId} node={NodeId} — saga {Strategy}", instanceId, nodeId, strategy);
            return;
        }

        if (compensation is not null)
        {
            // No saga engine wired (unit-test path) — record the start marker and stay Compensating.
            instance.SagaState = SagaState.Compensating;
            instance.SagaStrategy = compensation.Strategy;
            if (instance.CanTransitionTo(InstanceStatus.Compensating)) instance.TransitionTo(InstanceStatus.Compensating);
            _instances.Update(instance);
            await AppendEventAsync(seq, instance, "CompensationStarted", nodeId, state.NodeType, "{}", cancellationToken);
        }
        else
        {
            if (instance.CanTransitionTo(InstanceStatus.Failed)) instance.TransitionTo(InstanceStatus.Failed);
            instance.FailedAt = DateTime.UtcNow;
            instance.ErrorMessage = error;
            _instances.Update(instance);
            await AppendEventAsync(seq, instance, "InstanceFailed", nodeId, state.NodeType, ErrorPayload(error), cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("WorkflowOrchestrator.FailNodeAsync exit instance={InstanceId} node={NodeId}", instanceId, nodeId);
    }

    public async Task CancelAsync(Guid instanceId, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WorkflowOrchestrator.CancelAsync enter instance={InstanceId} by={RequestedBy}", instanceId, requestedBy);

        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' not found to cancel.");

        if (!instance.CanTransitionTo(InstanceStatus.Cancelled))
        {
            _logger.LogInformation("WorkflowOrchestrator.CancelAsync no-op — instance={InstanceId} already {Status}", instanceId, instance.Status);
            return;
        }

        instance.TransitionTo(InstanceStatus.Cancelled);
        instance.UpdatedBy = requestedBy;
        _instances.Update(instance);

        var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);
        await AppendEventAsync(seq, instance, "InstanceCancelled", null, null, "{}", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrEmpty(instance.WorkerLeaseId))
        {
            await _instances.ReleaseLeaseAsync(instanceId, instance.WorkerLeaseId, cancellationToken);
        }

        _logger.LogInformation("WorkflowOrchestrator.CancelAsync exit instance={InstanceId}", instanceId);
    }

    private async Task<WorkflowGraph> LoadGraphAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var version = await _versions.GetByIdForWorkspaceAsync(instance.WorkflowVersionId, instance.WorkspaceId, ct)
            ?? throw new InvalidOperationException(
                $"Pinned version '{instance.WorkflowVersionId}' missing for instance '{instance.Id}'.");
        return await _parser.ParseAsync(version.YamlContent, ct);
    }

    private async Task UpsertVariableAsync(WorkflowInstance instance, string name, string value, CancellationToken ct)
    {
        var existing = await _variables.GetByNameAsync(instance.Id, name, ct);
        if (existing is null)
        {
            await _variables.AddAsync(new WorkflowVariable
            {
                WorkspaceId = instance.WorkspaceId,
                InstanceId = instance.Id,
                Name = name,
                Value = value,
            }, ct);
        }
        else
        {
            existing.Value = value;
            _variables.Update(existing);
        }
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

    private static WorkflowNodeState NewState(WorkflowInstance instance, SfgNode node, NodeStatus status) => new()
    {
        WorkspaceId = instance.WorkspaceId,
        InstanceId = instance.Id,
        NodeId = node.Id,
        NodeType = node.Type.ToString(),
        Status = status,
        StartedAt = status is NodeStatus.Running or NodeStatus.Pending ? DateTime.UtcNow : null,
        CompletedAt = status == NodeStatus.Completed ? DateTime.UtcNow : null,
    };

    private static string ErrorPayload(string error) =>
        System.Text.Json.JsonSerializer.Serialize(new { error });

    private static string RetryPayload(string error, int attempt) =>
        System.Text.Json.JsonSerializer.Serialize(new { error, attempt });
}
