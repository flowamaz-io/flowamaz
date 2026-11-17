using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Runtime;
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
    // Optional so unit tests can construct the orchestrator without these; DI supplies them at
    // runtime. _sagaEngine: terminal failure with a compensation block runs the saga.
    private readonly ISagaEngine? _sagaEngine;
    // Runtime hooks (docs/ENGINE-RUNTIME-SPLIT.md §2). Defaults change nothing; the product registers
    // edition limits + archived workspaces (admission), audit + notifications (run events) and
    // development breakpoints (step interceptor), all in Application/Workflow/Hosting.
    private readonly IRunAdmission _admission;
    private readonly IRunEventSink _runEvents;
    private readonly IStepInterceptor _stepInterceptor;

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
        ISagaEngine? sagaEngine = null,
        IRunAdmission? admission = null,
        IRunEventSink? runEvents = null,
        IStepInterceptor? stepInterceptor = null)
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
        _admission = admission ?? AllowAllRunAdmission.Instance;
        _runEvents = runEvents ?? NullRunEventSink.Instance;
        _stepInterceptor = stepInterceptor ?? NoPauseStepInterceptor.Instance;
    }

    public async Task<WorkflowInstance> TriggerAsync(
        Guid workspaceId,
        Guid workflowDefinitionId,
        string? payload,
        string? idempotencyKey,
        InstanceTriggerType triggerType = InstanceTriggerType.Manual,
        string? correlationId = null,
        bool isTest = false,
        Guid? triggeredByUserId = null,
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

            // Admission hook: in the product, archived workspaces reject new runs (08-04) and the edition
            // caps real (non-test) runs per month (05-07). See ProductRunAdmission.
            await _admission.EnsureCanStartAsync(workspaceId, isTest, cancellationToken);

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
                if (definition.Status == WorkflowStatus.Archived)
                    throw WorkflowNotPublishedException.Archived(workflowDefinitionId);
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
                // Who started it: Runners see only their own runs (FUNCTIONAL.md §4). Null for webhook/API/schedule.
                CreatedBy = triggeredByUserId,
            };

            await _instances.AddAsync(instance, cancellationToken);
            await AppendEventAsync(1, instance, "InstanceStarted", null, null, instance.TriggerPayload ?? "{}", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _queue.EnqueueAsync(workspaceId, instance.Id, cancellationToken);

            await _runEvents.RunStartedAsync(new RunStartedEvent(
                workspaceId, instance.Id, definition.Id, definition.Name, triggeredByUserId, triggerType.ToString(), isTest),
                cancellationToken);

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

    /// <summary>
    /// Runs one change to a run under its row lock, in a transaction (joining the caller's, if one is open). A cancel,
    /// a gate decision and the worker's step then happen one after another, each seeing the others' result, instead
    /// of overwriting each other's status or writing the same event sequence number.
    /// </summary>
    private async Task<T> LockedAsync<T>(Guid instanceId, Func<Task<T>> change, CancellationToken ct)
    {
        if (_unitOfWork.HasActiveTransaction)
        {
            await _instances.LockForUpdateAsync(instanceId, ct);
            return await change();
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        await _instances.LockForUpdateAsync(instanceId, ct);
        var result = await change();
        if (transaction is not null) await transaction.CommitAsync(ct);
        return result;
    }

    private async Task LockedAsync(Guid instanceId, Func<Task<bool>> change, CancellationToken ct) =>
        await LockedAsync<bool>(instanceId, change, ct);

    /// <summary>A run that already ended (e.g. cancelled while a node was executing) takes no further changes.</summary>
    private bool HasEnded(WorkflowInstance instance, string operation, string nodeId)
    {
        if (instance.Status is not (InstanceStatus.Cancelled or InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Rejected)) return false;
        _logger.LogInformation("WorkflowOrchestrator.{Operation} ignored — instance={InstanceId} already {Status} (node {NodeId})",
            operation, instance.Id, instance.Status, nodeId);
        return true;
    }

    public Task<OrchestratorResult> StepAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default) =>
        LockedAsync(instanceId, () => StepCoreAsync(instanceId, leaseId, cancellationToken), cancellationToken);

    private async Task<OrchestratorResult> StepCoreAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("WorkflowOrchestrator.StepAsync enter instance={InstanceId} lease={LeaseId}", instanceId, leaseId);

        try
        {
            var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
                ?? throw new InvalidOperationException($"Instance '{instanceId}' not found while stepping.");

            if (instance.Status is InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Rejected or InstanceStatus.Cancelled)
            {
                return OrchestratorResult.Complete;
            }

            // Resuming from a breakpoint: the run was paused before a node; return it to Running so the
            // frontier can advance. The resumed (instance, node) was marked so it will not re-pause.
            if (instance.Status == InstanceStatus.BreakpointHit)
            {
                instance.TransitionTo(InstanceStatus.Running);
                _instances.Update(instance);
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

                // Expose the trigger payload to the workflow: the whole payload as {{ trigger }} and each
                // top-level field as its own variable ({{ variables.amount }}), so steps can use their input.
                await SeedTriggerVariablesAsync(instance, cancellationToken);
                // Persist now: condition evaluation later in this same step reads variables from the
                // database, so unsaved trigger variables would look empty to the first router.
                await _unitOfWork.SaveChangesAsync(cancellationToken);

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
                foreach (var target in await SelectTargetsAsync(instanceId, graph, source, cancellationToken))
                {
                    work.Enqueue(target);
                }
            }

            while (work.Count > 0)
            {
                var nodeId = work.Dequeue();
                if (!statedNodeIds.Add(nodeId)) continue; // already entered
                var node = graph.FindNode(nodeId);
                if (node is null) continue;

                // Development breakpoint: pause the run before this node executes. The instance is left
                // BreakpointHit with CurrentNodeId pointing at the node; resume re-enters StepAsync.
                if (_stepInterceptor.ShouldPause(instance.WorkspaceId, instance.WorkflowDefinitionId, instance.Id, node.Id))
                {
                    instance.TransitionTo(InstanceStatus.BreakpointHit);
                    instance.CurrentNodeId = node.Id;
                    _instances.Update(instance);
                    seq = await AppendEventAsync(seq, instance, "BreakpointHit", node.Id, node.Type.ToString(), "{}", cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation(
                        "WorkflowOrchestrator.StepAsync breakpoint — instance {InstanceId} paused at node {NodeId}",
                        instance.Id, node.Id);
                    return OrchestratorResult.Continue([]);
                }

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
                        foreach (var target in await SelectTargetsAsync(instanceId, graph, node.Id, cancellationToken))
                        {
                            work.Enqueue(target);
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
                        // Snapshot the (sensitive-stripped) variable context the node will execute against.
                        var inputSnapshot = await BuildInputSnapshotAsync(instance.Id, cancellationToken);
                        await _nodeStates.AddAsync(NewState(instance, node, NodeStatus.Pending), cancellationToken);
                        seq = await AppendEventAsync(seq, instance, "NodeStarted", node.Id, node.Type.ToString(), "{}", cancellationToken,
                            inputSnapshot: inputSnapshot);
                        nextNodes.Add(node.Id);
                        break;
                }
            }

            // Steps waiting for a retry: FailNodeAsync set them back to Pending and scheduled the run after
            // the backoff. The frontier above skips any node that already has a state, so hand them out
            // here, once their backoff has passed (a sibling finishing early can step the run sooner).
            var now = DateTime.UtcNow;
            foreach (var state in states.Where(s => s.Status == NodeStatus.Pending && s.RetryCount > 0))
            {
                if (nextNodes.Contains(state.NodeId)) continue;
                var node = graph.FindNode(state.NodeId);
                if (node is null || node.Type == NodeType.HumanGate) continue;
                var dueAt = (state.LastRetryAt ?? now).AddSeconds(node.RetryPolicy is null ? 0 : RetryDelaySeconds(node.RetryPolicy, state.RetryCount));
                if (dueAt > now.AddSeconds(RetryDueToleranceSeconds)) continue;

                var inputSnapshot = await BuildInputSnapshotAsync(instance.Id, cancellationToken);
                seq = await AppendEventAsync(seq, instance, "NodeStarted", node.Id, node.Type.ToString(),
                    $$"""{"attempt":{{state.RetryCount + 1}}}""", cancellationToken, inputSnapshot: inputSnapshot);
                nextNodes.Add(node.Id);
                _logger.LogInformation(
                    "WorkflowOrchestrator.StepAsync retry dispatched instance={InstanceId} node={NodeId} attempt={Attempt}",
                    instance.Id, node.Id, state.RetryCount + 1);
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

            // Once the instance commits as Completed (the product notifies the run's creator).
            if (isComplete)
            {
                var completedDefinition = await _definitions.GetByIdForWorkspaceAsync(instance.WorkflowDefinitionId, instance.WorkspaceId, cancellationToken);
                await _runEvents.RunCompletedAsync(
                    new RunCompletedEvent(instance.WorkspaceId, instance.Id, instance.CreatedBy, completedDefinition?.Name), cancellationToken);
            }

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

    public Task CompleteNodeAsync(Guid instanceId, string nodeId, string output, string leaseId, CancellationToken cancellationToken = default) =>
        LockedAsync(instanceId, () => CompleteNodeCoreAsync(instanceId, nodeId, output, cancellationToken), cancellationToken);

    private async Task<bool> CompleteNodeCoreAsync(Guid instanceId, string nodeId, string output, CancellationToken cancellationToken)
    {
        _logger.LogInformation("WorkflowOrchestrator.CompleteNodeAsync enter instance={InstanceId} node={NodeId}", instanceId, nodeId);

        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' not found completing node '{nodeId}'.");
        if (HasEnded(instance, "CompleteNodeAsync", nodeId)) return false;
        var state = await _nodeStates.GetByNodeAsync(instanceId, nodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Node state '{nodeId}' not found on instance '{instanceId}'.");

        state.Status = NodeStatus.Completed;
        state.CompletedAt = DateTime.UtcNow;
        state.OutputPayload = string.IsNullOrWhiteSpace(output) ? null : output;
        _nodeStates.Update(state);

        // A decided gate resumes a parked instance.
        if (instance.Status == InstanceStatus.Waiting)
        {
            instance.TransitionTo(InstanceStatus.Running);
            _instances.Update(instance);
        }

        await UpsertVariableAsync(instance, $"{nodeId}.output", string.IsNullOrWhiteSpace(output) ? "null" : output, cancellationToken);

        // Snapshot what this node produced + how long it ran (07-04 step debugger). The output is
        // already non-secret (node outputs are not credential values); record it as-is.
        var durationMs = state.StartedAt is not null
            ? (int)Math.Clamp((DateTime.UtcNow - state.StartedAt.Value).TotalMilliseconds, 0, int.MaxValue)
            : 0;

        var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);
        await AppendEventAsync(seq, instance, "NodeCompleted", nodeId, state.NodeType, state.OutputPayload ?? "{}", cancellationToken,
            outputSnapshot: state.OutputPayload ?? "{}", durationMs: durationMs);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // More steps remain — put the instance back on its queue for the next frontier.
        await _queue.EnqueueAsync(instance.WorkspaceId, instanceId, cancellationToken);
        _logger.LogInformation("WorkflowOrchestrator.CompleteNodeAsync exit instance={InstanceId} node={NodeId}", instanceId, nodeId);
        return true;
    }

    public Task FailNodeAsync(Guid instanceId, string nodeId, string error, string leaseId, CancellationToken cancellationToken = default,
        bool retryable = true) =>
        LockedAsync(instanceId, () => FailNodeCoreAsync(instanceId, nodeId, error, retryable, rejected: false, cancellationToken), cancellationToken);

    public async Task RejectNodeAsync(Guid instanceId, string nodeId, string reason, string leaseId, CancellationToken cancellationToken = default)
    {
        var ended = await LockedAsync<bool>(instanceId,
            () => FailNodeCoreAsync(instanceId, nodeId, reason, retryable: false, rejected: true, cancellationToken), cancellationToken);
        if (!ended) return;

        // Once committed as Rejected (compensation may have kept it Compensating in a host without a saga engine).
        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken);
        if (instance?.Status != InstanceStatus.Rejected) return;
        var definition = await _definitions.GetByIdForWorkspaceAsync(instance.WorkflowDefinitionId, instance.WorkspaceId, cancellationToken);
        await _runEvents.RunRejectedAsync(new RunRejectedEvent(
            instance.WorkspaceId, instance.Id, instance.WorkflowDefinitionId, definition?.Name, instance.CreatedBy, nodeId, reason,
            instance.IsTest), cancellationToken);
    }

    private async Task<bool> FailNodeCoreAsync(Guid instanceId, string nodeId, string error, bool retryable, bool rejected,
        CancellationToken cancellationToken)
    {
        if (rejected) retryable = false;
        _logger.LogInformation("WorkflowOrchestrator.FailNodeAsync enter instance={InstanceId} node={NodeId}", instanceId, nodeId);

        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' not found failing node '{nodeId}'.");
        if (HasEnded(instance, "FailNodeAsync", nodeId)) return false;
        var state = await _nodeStates.GetByNodeAsync(instanceId, nodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Node state '{nodeId}' not found on instance '{instanceId}'.");

        var graph = await LoadGraphAsync(instance, cancellationToken);
        var node = graph.FindNode(nodeId);
        var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);

        var retry = node?.RetryPolicy;
        // max_attempts counts every try, the first included: max_attempts 3 = the first try + 2 retries.
        if (retryable && retry is not null && state.RetryCount + 1 < retry.MaxAttempts)
        {
            state.RetryCount++;
            state.LastRetryAt = DateTime.UtcNow;
            state.Status = NodeStatus.Pending;
            state.ErrorMessage = error;
            _nodeStates.Update(state);
            seq = await AppendEventAsync(seq, instance, "NodeFailed", nodeId, state.NodeType, RetryPayload(error, state.RetryCount), cancellationToken,
                errorSnapshot: ErrorSnapshot(error));
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var delay = RetryDelaySeconds(retry, state.RetryCount);
            await _queue.EnqueueDelayedAsync(instance.WorkspaceId, instanceId, delay, cancellationToken);
            _logger.LogWarning(
                "WorkflowOrchestrator.FailNodeAsync retry instance={InstanceId} node={NodeId} attempt={Attempt} delay={Delay}s",
                instanceId, nodeId, state.RetryCount, delay);
            return true;
        }

        // Out of tries: say so, so "HTTP 500" isn't read as a single attempt.
        if (retryable && retry is not null && retry.MaxAttempts > 1)
        {
            var tries = state.RetryCount + 1;
            error = $"Failed after {tries} {(tries == 1 ? "try" : "tries")}: {error}";
        }

        state.Status = NodeStatus.Failed;
        state.ErrorMessage = error;
        _nodeStates.Update(state);
        // The step's duration (first start to now) on its final event, as NodeCompleted carries for success.
        int? failedDurationMs = state.StartedAt is { } startedAt
            ? (int)Math.Clamp((DateTime.UtcNow - startedAt).TotalMilliseconds, 0, int.MaxValue)
            : null;
        seq = await AppendEventAsync(seq, instance, "NodeFailed", nodeId, state.NodeType, ErrorPayload(error), cancellationToken,
            errorSnapshot: ErrorSnapshot(error), durationMs: failedDurationMs);

        var compensation = node?.Compensation;
        if (compensation is not null && _sagaEngine is not null)
        {
            // Persist the node failure, then hand off to the saga engine which drives the chosen
            // strategy and records CompensationStarted → CompensationCompleted/Failed itself.
            if (rejected)
            {
                instance.ErrorMessage = error; // the run's reason ("Gate rejected: …") survives compensation
                _instances.Update(instance);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var strategy = Enum.TryParse<SagaStrategyType>(compensation.Strategy, ignoreCase: true, out var parsed)
                ? parsed : SagaStrategyType.Backward;
            await _sagaEngine.StartAsync(instanceId, nodeId, strategy, cancellationToken,
                rejected ? InstanceStatus.Rejected : InstanceStatus.Failed);
            _logger.LogInformation("WorkflowOrchestrator.FailNodeAsync exit instance={InstanceId} node={NodeId} — saga {Strategy}", instanceId, nodeId, strategy);
            return true;
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
            // A rejection is a business outcome, not an error (B10): Rejected, ended at FailedAt, no error snapshot.
            var end = rejected ? InstanceStatus.Rejected : InstanceStatus.Failed;
            if (instance.CanTransitionTo(end)) instance.TransitionTo(end);
            instance.FailedAt = DateTime.UtcNow;
            instance.ErrorMessage = error;
            _instances.Update(instance);
            if (rejected)
                await AppendEventAsync(seq, instance, "InstanceRejected", nodeId, state.NodeType, ErrorPayload(error), cancellationToken);
            else
                await AppendEventAsync(seq, instance, "InstanceFailed", nodeId, state.NodeType, ErrorPayload(error), cancellationToken,
                    errorSnapshot: ErrorSnapshot(error));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("WorkflowOrchestrator.FailNodeAsync exit instance={InstanceId} node={NodeId}", instanceId, nodeId);
        return true;
    }

    public Task ParkAtGateAsync(Guid instanceId, string nodeId, Guid gateId, CancellationToken cancellationToken = default) =>
        LockedAsync(instanceId, () => ParkAtGateCoreAsync(instanceId, nodeId, gateId, cancellationToken), cancellationToken);

    private async Task<bool> ParkAtGateCoreAsync(Guid instanceId, string nodeId, Guid gateId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("WorkflowOrchestrator.ParkAtGateAsync enter instance={InstanceId} node={NodeId} gate={GateId}",
            instanceId, nodeId, gateId);

        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' not found parking at gate '{nodeId}'.");
        if (HasEnded(instance, "ParkAtGateAsync", nodeId)) return false;
        var state = await _nodeStates.GetByNodeAsync(instanceId, nodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Node state '{nodeId}' not found on instance '{instanceId}'.");

        if (state.Status == NodeStatus.Pending)
        {
            state.Status = NodeStatus.Running;
            state.StartedAt ??= DateTime.UtcNow;
            _nodeStates.Update(state);
        }

        if (instance.CanTransitionTo(InstanceStatus.Waiting)) instance.TransitionTo(InstanceStatus.Waiting);
        instance.CurrentNodeId = nodeId;
        _instances.Update(instance);

        var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);
        await AppendEventAsync(seq, instance, "GateWaiting", nodeId, state.NodeType, $$"""{"gateId":"{{gateId}}"}""", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("WorkflowOrchestrator.ParkAtGateAsync exit instance={InstanceId} node={NodeId}", instanceId, nodeId);
        return true;
    }

    public Task CancelAsync(Guid instanceId, Guid requestedBy, CancellationToken cancellationToken = default, string? cancelledByLabel = null) =>
        LockedAsync(instanceId, () => CancelCoreAsync(instanceId, requestedBy, cancelledByLabel, cancellationToken), cancellationToken);

    private async Task<bool> CancelCoreAsync(Guid instanceId, Guid requestedBy, string? cancelledByLabel, CancellationToken cancellationToken)
    {
        _logger.LogInformation("WorkflowOrchestrator.CancelAsync enter instance={InstanceId} by={RequestedBy}", instanceId, requestedBy);

        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Instance '{instanceId}' not found to cancel.");

        if (!instance.CanTransitionTo(InstanceStatus.Cancelled))
        {
            _logger.LogInformation("WorkflowOrchestrator.CancelAsync no-op — instance={InstanceId} already {Status}", instanceId, instance.Status);
            return false;
        }

        var now = DateTime.UtcNow;
        instance.TransitionTo(InstanceStatus.Cancelled);
        // B6: a cancelled run has ended — stamp the end time so the run's duration stops counting.
        instance.CompletedAt ??= now;
        instance.UpdatedBy = requestedBy;
        _instances.Update(instance);

        var seq = await _events.GetNextSequenceNumberAsync(instanceId, cancellationToken);

        // B6: close every node still in flight (e.g. a gate waiting for a decision) so it no longer
        // reads "Running" with an ever-growing duration on the timeline.
        var states = await _nodeStates.GetForInstanceAsync(instanceId, cancellationToken) ?? [];
        var closed = 0;
        foreach (var state in states.Where(s => s.Status is NodeStatus.Pending or NodeStatus.Running))
        {
            state.Status = NodeStatus.Cancelled;
            state.CompletedAt ??= now;
            _nodeStates.Update(state);
            seq = await AppendEventAsync(seq, instance, "NodeCancelled", state.NodeId, state.NodeType, "{}", cancellationToken);
            closed++;
        }

        await AppendEventAsync(seq, instance, "InstanceCancelled", null, null, "{}", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("WorkflowOrchestrator.CancelAsync closed {Count} in-flight node(s) instance={InstanceId}", closed, instanceId);

        if (!string.IsNullOrEmpty(instance.WorkerLeaseId))
        {
            await _instances.ReleaseLeaseAsync(instanceId, instance.WorkerLeaseId, cancellationToken);
        }

        // Who stopped the run: the signed-in user, or (Guid.Empty) the API key of a public-API call, which
        // AuditService attributes from the request. A withdrawn loan application must be traceable.
        var definition = await _definitions.GetByIdForWorkspaceAsync(instance.WorkflowDefinitionId, instance.WorkspaceId, cancellationToken);
        Guid? actor = requestedBy == Guid.Empty ? null : requestedBy;
        var cancelledBy = actor.HasValue || string.IsNullOrWhiteSpace(cancelledByLabel) ? null : cancelledByLabel.Trim();
        await _runEvents.RunCancelledAsync(new RunCancelledEvent(
            instance.WorkspaceId, instance.Id, instance.WorkflowDefinitionId, definition?.Name, actor, cancelledBy, closed, instance.IsTest),
            cancellationToken);

        _logger.LogInformation("WorkflowOrchestrator.CancelAsync exit instance={InstanceId}", instanceId);
        return true;
    }

    private async Task<WorkflowGraph> LoadGraphAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var version = await _versions.GetByIdForWorkspaceAsync(instance.WorkflowVersionId, instance.WorkspaceId, ct)
            ?? throw new InvalidOperationException(
                $"Pinned version '{instance.WorkflowVersionId}' missing for instance '{instance.Id}'.");
        return await _parser.ParseAsync(version.YamlContent, ct);
    }

    /// <summary>
    /// Targets of a node's outgoing edges whose conditions hold. Default ("else") edges are followed
    /// only when no conditional sibling matched — otherwise a router would take every branch.
    /// </summary>
    private async Task<List<string>> SelectTargetsAsync(Guid instanceId, WorkflowGraph graph, string nodeId, CancellationToken ct)
    {
        var targets = new List<string>();
        var defaults = new List<string>();
        var anyConditionalMatched = false;
        var outgoing = graph.OutgoingEdges(nodeId).ToList();

        // Label-only branches (via: approved / rejected) route on the upstream decision. Without this a
        // router with no branch config would take every edge (e.g. approve AND notify-rejected).
        string? decision = null;
        if (outgoing.Any(IsLabelOnly))
            decision = await UpstreamDecisionAsync(instanceId, graph, nodeId, ct);

        foreach (var edge in outgoing)
        {
            if (edge.IsDefault)
            {
                defaults.Add(edge.ToNodeId);
                continue;
            }
            if (IsLabelOnly(edge) && decision is not null)
            {
                if (DecisionMatches(edge.Via!, decision))
                {
                    targets.Add(edge.ToNodeId);
                    anyConditionalMatched = true;
                }
                continue;
            }
            // An approval step's unlabelled link next to labelled branches is its main path: "approved" only (B10b).
            if (decision is not null && Core.Workflow.Twin.TwinSimulator.IsApprovedOnlyLink(graph, nodeId, edge))
            {
                if (DecisionMatches(Core.Workflow.Twin.TwinSimulator.Approved, decision))
                {
                    targets.Add(edge.ToNodeId);
                    anyConditionalMatched = true;
                }
                continue;
            }
            if (await _variableEvaluation.EvaluateConditionAsync(instanceId, edge.Condition, ct))
            {
                targets.Add(edge.ToNodeId);
                if (!string.IsNullOrWhiteSpace(edge.Condition)) anyConditionalMatched = true;
            }
        }
        if (!anyConditionalMatched) targets.AddRange(defaults);
        return targets;
    }

    private static bool IsLabelOnly(SfgEdge edge) =>
        !edge.IsDefault && string.IsNullOrWhiteSpace(edge.Condition) && !string.IsNullOrWhiteSpace(edge.Via);

    /// <summary>The "decision" field of this node's output, else of its nearest predecessor that has one.</summary>
    private async Task<string?> UpstreamDecisionAsync(Guid instanceId, WorkflowGraph graph, string nodeId, CancellationToken ct)
    {
        foreach (var candidate in new[] { nodeId }.Concat(graph.IncomingEdges(nodeId).Select(e => e.FromNodeId)))
        {
            var variable = await _variables.GetByNameAsync(instanceId, $"{candidate}.output", ct);
            if (variable is null || string.IsNullOrWhiteSpace(variable.Value)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(variable.Value);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("decision", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String)
                    return d.GetString();
            }
            catch (System.Text.Json.JsonException) { }
        }
        return null;
    }

    // One definition shared with the Workflow Twin simulator, so simulated runs follow decisions the same way.
    internal static bool DecisionMatches(string via, string decision) =>
        Core.Workflow.Twin.TwinSimulator.DecisionMatches(via, decision);

    public async Task<bool> HasDecisionRouteAsync(Guid instanceId, string nodeId, string decision, CancellationToken cancellationToken = default)
    {
        var instance = await _instances.GetByIdAsync(instanceId, cancellationToken);
        if (instance is null) return false;
        var graph = await LoadGraphAsync(instance, cancellationToken);
        return Core.Workflow.Twin.TwinSimulator.HasDecisionRoute(graph, nodeId, decision);
    }

    private async Task SeedTriggerVariablesAsync(WorkflowInstance instance, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instance.TriggerPayload)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(instance.TriggerPayload);
            await UpsertVariableAsync(instance, "trigger", doc.RootElement.GetRawText(), ct);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name.Length is 0 or > 100 || property.Name.Contains('.')) continue;
                await UpsertVariableAsync(instance, property.Name, property.Value.GetRawText(), ct);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Non-JSON payloads stay available as a plain string.
            await UpsertVariableAsync(instance, "trigger", System.Text.Json.JsonSerializer.Serialize(instance.TriggerPayload), ct);
        }
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
        long sequenceNumber, WorkflowInstance instance, string eventType, string? nodeId, string? nodeType, string payload, CancellationToken ct,
        string? inputSnapshot = null, string? outputSnapshot = null, string? errorSnapshot = null, int? durationMs = null)
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
            InputSnapshot = inputSnapshot,
            OutputSnapshot = outputSnapshot,
            ErrorSnapshot = errorSnapshot,
            DurationMs = durationMs,
            OccurredAt = DateTime.UtcNow,
        }, ct);
        return sequenceNumber + 1;
    }

    /// <summary>
    /// Builds the per-node input snapshot from the instance's current variable context, with
    /// sensitive variables (<see cref="WorkflowVariable.IsSensitive"/>) stripped entirely so they
    /// never reach the step-debugger snapshot. Returns a jsonb object string.
    /// </summary>
    private async Task<string> BuildInputSnapshotAsync(Guid instanceId, CancellationToken ct)
    {
        var variables = await _variables.GetForInstanceAsync(instanceId, ct);
        var safe = new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.Ordinal);
        foreach (var v in variables ?? [])
        {
            if (v.IsSensitive) continue; // never snapshot sensitive values
            safe[v.Name] = TryParseJsonValue(v.Value);
        }
        return System.Text.Json.JsonSerializer.Serialize(safe);
    }

    private static System.Text.Json.Nodes.JsonNode? TryParseJsonValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return System.Text.Json.Nodes.JsonNode.Parse(raw); }
        catch (System.Text.Json.JsonException) { return System.Text.Json.Nodes.JsonValue.Create(raw); }
    }

    private static WorkflowNodeState NewState(WorkflowInstance instance, SfgNode node, NodeStatus status) => new()
    {
        WorkspaceId = instance.WorkspaceId,
        InstanceId = instance.Id,
        NodeId = node.Id,
        NodeType = node.Type.ToString(),
        Status = status,
        // O3: instantly-completed nodes (trigger, routers, end) get a start time too, so timelines
        // ordered by StartedAt follow execution order instead of falling back to the save-time CreatedAt.
        StartedAt = status is NodeStatus.Running or NodeStatus.Pending or NodeStatus.Completed ? DateTime.UtcNow : null,
        CompletedAt = status == NodeStatus.Completed ? DateTime.UtcNow : null,
    };

    private static string ErrorPayload(string error) =>
        System.Text.Json.JsonSerializer.Serialize(new { error });

    /// <summary>
    /// Error snapshot for the step debugger: message + type ONLY. Never includes a stack trace — the
    /// node executor passes only the message string, so this is safe to surface to the client.
    /// </summary>
    private static string ErrorSnapshot(string error) =>
        System.Text.Json.JsonSerializer.Serialize(new { message = error, type = "NodeExecutionError" });

    /// <summary>A delayed run can wake a moment before its backoff ends (clock skew between machines).</summary>
    private const int RetryDueToleranceSeconds = 2;

    /// <summary>Wait before retry number <paramref name="retryNumber"/> (1 = the first retry).</summary>
    internal static int RetryDelaySeconds(RetryPolicy retry, int retryNumber) =>
        (int)Math.Round(Math.Max(0, retry.BackoffSeconds) * Math.Pow(retry.BackoffMultiplier <= 0 ? 1 : retry.BackoffMultiplier, Math.Max(0, retryNumber - 1)));

    private static string RetryPayload(string error, int attempt) =>
        System.Text.Json.JsonSerializer.Serialize(new { error, attempt });
}
