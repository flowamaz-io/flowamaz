using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Workflow;

/// <summary>
/// A single durable execution of a workflow. The version is pinned at trigger time and never
/// changes. Ownership is leased to a worker via <see cref="WorkerLeaseId"/> /
/// <see cref="WorkerLeaseExpiresAt"/>; an expired lease lets another worker reclaim the instance.
/// </summary>
public class WorkflowInstance : WorkspaceEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public Guid WorkflowVersionId { get; set; }

    /// <summary>Which workspace environment this run belongs to. Gates the step debugger (Production is off-limits).</summary>
    public WorkspaceEnvironmentType EnvironmentType { get; set; } = WorkspaceEnvironmentType.Dev;
    public InstanceStatus Status { get; set; } = InstanceStatus.Pending;
    public InstanceTriggerType TriggerType { get; set; } = InstanceTriggerType.Manual;
    public string? TriggerPayload { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CurrentNodeId { get; set; }
    public string? WorkerLeaseId { get; set; }
    public DateTime? WorkerLeaseExpiresAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public SagaState SagaState { get; set; } = SagaState.None;
    public string? SagaStrategy { get; set; }

    /// <summary>Whether <see cref="Status"/> may legally move to <paramref name="target"/>.</summary>
    public bool CanTransitionTo(InstanceStatus target) =>
        AllowedTransitions.TryGetValue(Status, out var allowed) && allowed.Contains(target);

    /// <summary>
    /// Moves to <paramref name="target"/> if the transition is legal; throws otherwise.
    /// Terminal states (Completed/Failed/Cancelled) reject all transitions.
    /// </summary>
    public void TransitionTo(InstanceStatus target)
    {
        if (!CanTransitionTo(target))
        {
            throw new InvalidOperationException(
                $"Illegal instance status transition {Status} → {target} for instance {Id}.");
        }

        Status = target;
    }

    private static readonly IReadOnlyDictionary<InstanceStatus, InstanceStatus[]> AllowedTransitions =
        new Dictionary<InstanceStatus, InstanceStatus[]>
        {
            [InstanceStatus.Pending] = [InstanceStatus.Running, InstanceStatus.Cancelled],
            [InstanceStatus.Running] =
            [
                InstanceStatus.Waiting, InstanceStatus.Completed, InstanceStatus.Failed,
                InstanceStatus.Cancelled, InstanceStatus.Compensating,
            ],
            [InstanceStatus.Waiting] =
            [
                InstanceStatus.Running, InstanceStatus.Failed,
                InstanceStatus.Cancelled, InstanceStatus.Compensating,
            ],
            // Running is reachable from Compensating for the Forward saga strategy (resume execution).
            [InstanceStatus.Compensating] = [InstanceStatus.Completed, InstanceStatus.Failed, InstanceStatus.Running],
            [InstanceStatus.Completed] = [],
            [InstanceStatus.Failed] = [InstanceStatus.Compensating],
            [InstanceStatus.Cancelled] = [],
        };
}
