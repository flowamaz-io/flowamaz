namespace Flowamaz.Core.Interfaces.Queue;

/// <summary>
/// Redis-backed work queue for instance execution. Per-workspace pending/processing lists give
/// fair isolation; a global delayed sorted-set holds retry/backoff items. The claim is atomic
/// (RPOPLPUSH pending → processing) so two workers can never grab the same instance.
/// </summary>
public interface ITaskQueue
{
    /// <summary>LPUSH the instance onto its workspace pending list and mark the workspace active.</summary>
    Task EnqueueAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>ZADD the instance to the delayed set with a score of now + <paramref name="delaySeconds"/>.</summary>
    Task EnqueueDelayedAsync(Guid workspaceId, Guid instanceId, int delaySeconds, CancellationToken cancellationToken = default);

    /// <summary>Atomically move one instance from the workspace pending list to its processing list. Null if empty.</summary>
    Task<Guid?> DequeueAsync(Guid workspaceId, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>Remove the instance from the processing list — it is done (or handed off to a gate).</summary>
    Task AcknowledgeAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Move the instance from processing back to pending — more steps remain.</summary>
    Task ReturnToQueueAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Pop all delayed items whose execute-after time has passed (ZRANGEBYSCORE + ZREM).</summary>
    Task<IReadOnlyList<DelayedQueueItem>> GetDelayedReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>Workspaces that currently have queued work, so the worker knows which lists to poll.</summary>
    Task<IReadOnlyList<Guid>> GetActiveWorkspacesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A delayed-queue entry promoted to pending once its time arrives.</summary>
public sealed record DelayedQueueItem(Guid WorkspaceId, Guid InstanceId);
