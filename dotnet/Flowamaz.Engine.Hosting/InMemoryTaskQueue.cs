using Flowamaz.Core.Interfaces.Queue;

namespace Flowamaz.Infrastructure.Queue;

/// <summary>
/// <see cref="ITaskQueue"/> in process memory, for a single host process (tests, trying the engine out,
/// small self-hosted setups). Same shape as <see cref="RedisTaskQueue"/>: a pending and a processing list
/// per workspace (so one busy workspace doesn't starve the others), and a delayed set for retries that
/// <see cref="DelayedQueuePromoterService"/> moves back to pending once due. Nothing survives a restart:
/// on start-up the worker's orphan sweep and the database's Pending runs are what's left.
/// </summary>
public sealed class InMemoryTaskQueue : ITaskQueue
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, LinkedList<Guid>> _pending = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _processing = new();
    private readonly List<(DateTime Due, Guid WorkspaceId, Guid InstanceId)> _delayed = new();
    private readonly TimeProvider _time;

    public InMemoryTaskQueue(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public Task EnqueueAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var list = Pending(workspaceId);
            if (!list.Contains(instanceId)) list.AddLast(instanceId);
        }
        return Task.CompletedTask;
    }

    public Task EnqueueDelayedAsync(Guid workspaceId, Guid instanceId, int delaySeconds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _delayed.RemoveAll(d => d.InstanceId == instanceId);
            _delayed.Add((_time.GetUtcNow().UtcDateTime.AddSeconds(Math.Max(0, delaySeconds)), workspaceId, instanceId));
        }
        return Task.CompletedTask;
    }

    public Task<Guid?> DequeueAsync(Guid workspaceId, string leaseId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var list = Pending(workspaceId);
            if (list.First is null) return Task.FromResult<Guid?>(null);
            var id = list.First.Value;
            list.RemoveFirst();
            Processing(workspaceId).Add(id);
            return Task.FromResult<Guid?>(id);
        }
    }

    public Task AcknowledgeAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        lock (_lock) Processing(workspaceId).Remove(instanceId);
        return Task.CompletedTask;
    }

    public Task ReturnToQueueAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Processing(workspaceId).Remove(instanceId);
            var list = Pending(workspaceId);
            if (!list.Contains(instanceId)) list.AddFirst(instanceId);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DelayedQueueItem>> GetDelayedReadyAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var ready = _delayed.Where(d => d.Due <= now).ToList();
            _delayed.RemoveAll(d => d.Due <= now);
            return Task.FromResult<IReadOnlyList<DelayedQueueItem>>(
                ready.Select(d => new DelayedQueueItem(d.WorkspaceId, d.InstanceId)).ToList());
        }
    }

    public Task<IReadOnlyList<Guid>> GetActiveWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(_pending.Where(p => p.Value.Count > 0).Select(p => p.Key).ToList());
        }
    }

    private LinkedList<Guid> Pending(Guid workspaceId) =>
        _pending.TryGetValue(workspaceId, out var list) ? list : _pending[workspaceId] = new LinkedList<Guid>();

    private HashSet<Guid> Processing(Guid workspaceId) =>
        _processing.TryGetValue(workspaceId, out var set) ? set : _processing[workspaceId] = new HashSet<Guid>();
}
