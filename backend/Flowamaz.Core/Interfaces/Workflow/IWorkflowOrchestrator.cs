using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>
/// The durable execution core. Triggering creates a Pending instance (pinned to a version) and
/// enqueues it; the worker then drives it one <see cref="StepAsync"/> at a time, recording every
/// transition to the append-only event log. All operations are workspace-scoped.
/// </summary>
public interface IWorkflowOrchestrator
{
    /// <summary>
    /// Creates and enqueues a new instance. Production runs require a published version; test runs
    /// (<paramref name="isTest"/>=true) accept draft workflows and expire after 24 hours.
    /// If <paramref name="idempotencyKey"/> matches an existing instance, that instance is returned
    /// and nothing new is created (exactly-once trigger).
    /// </summary>
    Task<WorkflowInstance> TriggerAsync(
        Guid workspaceId,
        Guid workflowDefinitionId,
        string? payload,
        string? idempotencyKey,
        InstanceTriggerType triggerType = InstanceTriggerType.Manual,
        string? correlationId = null,
        bool isTest = false,
        Guid? triggeredByUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Advances the instance: figures out the next node(s) from the graph and records transitions.</summary>
    Task<OrchestratorResult> StepAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>Marks a node Completed, stores its output as a variable, and appends a NodeCompleted event.</summary>
    Task CompleteNodeAsync(Guid instanceId, string nodeId, string output, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a node Failed; re-queues with backoff if <paramref name="retryable"/> and retries remain,
    /// else begins compensation if configured. Permanent failures (bad input, rejected credential,
    /// gate rejection/timeout) pass <c>retryable: false</c> so the RetryPolicy is not consumed.
    /// </summary>
    Task FailNodeAsync(Guid instanceId, string nodeId, string error, string leaseId, CancellationToken cancellationToken = default,
        bool retryable = true);

    /// <summary>
    /// Parks the instance at a human gate: the node stays Running, the instance becomes Waiting and a
    /// GateWaiting event is appended. The decision later calls Complete/FailNodeAsync to resume.
    /// </summary>
    Task ParkAtGateAsync(Guid instanceId, string nodeId, Guid gateId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the instance, appends the event, and releases any worker lease.</summary>
    Task CancelAsync(Guid instanceId, Guid requestedBy, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a single <see cref="IWorkflowOrchestrator.StepAsync"/> call.</summary>
public sealed record OrchestratorResult(IReadOnlyList<string> NextNodes, bool IsComplete, bool NeedsHumanGate)
{
    public static OrchestratorResult Complete { get; } = new([], IsComplete: true, NeedsHumanGate: false);
    public static OrchestratorResult Gate(IReadOnlyList<string> nodes) => new(nodes, IsComplete: false, NeedsHumanGate: true);
    public static OrchestratorResult Continue(IReadOnlyList<string> nodes) => new(nodes, IsComplete: false, NeedsHumanGate: false);
}
