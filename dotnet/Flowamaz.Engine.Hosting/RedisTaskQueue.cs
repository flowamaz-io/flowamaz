using Flowamaz.Core.Interfaces.Queue;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Queue;

/// <summary>
/// Redis-backed implementation of <see cref="ITaskQueue"/>. Per-workspace pending/processing lists
/// isolate tenants; the claim uses RPOPLPUSH so it is atomic — two workers can never pop the same
/// instance (acceptance criterion). A global sorted set holds delayed (retry/backoff) items.
/// StackExchange.Redis multiplexing does not expose blocking BRPOPLPUSH, so the worker polls.
/// </summary>
public sealed class RedisTaskQueue : ITaskQueue
{
    private const string Prefix = "fmz:queue";
    private static readonly RedisKey DelayedKey = $"{Prefix}:delayed";
    private static readonly RedisKey ActiveWorkspacesKey = $"{Prefix}:workspaces";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisTaskQueue> _logger;

    public RedisTaskQueue(IConnectionMultiplexer redis, ILogger<RedisTaskQueue> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private static RedisKey PendingKey(Guid workspaceId) => $"{Prefix}:{workspaceId}:pending";
    private static RedisKey ProcessingKey(Guid workspaceId) => $"{Prefix}:{workspaceId}:processing";

    public async Task EnqueueAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        await db.ListLeftPushAsync(PendingKey(workspaceId), instanceId.ToString());
        await db.SetAddAsync(ActiveWorkspacesKey, workspaceId.ToString());
        _logger.LogDebug("RedisTaskQueue.EnqueueAsync workspace={WorkspaceId} instance={InstanceId}", workspaceId, instanceId);
    }

    public async Task EnqueueDelayedAsync(Guid workspaceId, Guid instanceId, int delaySeconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executeAfter = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, delaySeconds)).ToUnixTimeSeconds();
        var db = _redis.GetDatabase();
        await db.SortedSetAddAsync(DelayedKey, $"{workspaceId}:{instanceId}", executeAfter);
        _logger.LogDebug(
            "RedisTaskQueue.EnqueueDelayedAsync workspace={WorkspaceId} instance={InstanceId} delay={Delay}s",
            workspaceId, instanceId, delaySeconds);
    }

    public async Task<Guid?> DequeueAsync(Guid workspaceId, string leaseId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var value = await db.ListRightPopLeftPushAsync(PendingKey(workspaceId), ProcessingKey(workspaceId));
        if (value.IsNullOrEmpty) return null;

        if (!Guid.TryParse(value.ToString(), out var instanceId))
        {
            _logger.LogWarning("RedisTaskQueue.DequeueAsync discarded non-Guid payload {Payload}", value.ToString());
            await db.ListRemoveAsync(ProcessingKey(workspaceId), value);
            return null;
        }

        _logger.LogDebug(
            "RedisTaskQueue.DequeueAsync claimed instance={InstanceId} workspace={WorkspaceId} lease={LeaseId}",
            instanceId, workspaceId, leaseId);
        return instanceId;
    }

    public async Task AcknowledgeAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        await db.ListRemoveAsync(ProcessingKey(workspaceId), instanceId.ToString());
    }

    public async Task ReturnToQueueAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        await db.ListRemoveAsync(ProcessingKey(workspaceId), instanceId.ToString());
        await db.ListLeftPushAsync(PendingKey(workspaceId), instanceId.ToString());
        await db.SetAddAsync(ActiveWorkspacesKey, workspaceId.ToString());
    }

    public async Task<IReadOnlyList<DelayedQueueItem>> GetDelayedReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ready = await db.SortedSetRangeByScoreAsync(DelayedKey, double.NegativeInfinity, now);

        var items = new List<DelayedQueueItem>(ready.Length);
        foreach (var member in ready)
        {
            await db.SortedSetRemoveAsync(DelayedKey, member);
            var parts = member.ToString().Split(':', 2);
            if (parts.Length == 2 && Guid.TryParse(parts[0], out var ws) && Guid.TryParse(parts[1], out var instance))
            {
                items.Add(new DelayedQueueItem(ws, instance));
            }
        }

        return items;
    }

    public async Task<IReadOnlyList<Guid>> GetActiveWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var members = await db.SetMembersAsync(ActiveWorkspacesKey);
        var result = new List<Guid>(members.Length);
        foreach (var m in members)
        {
            if (Guid.TryParse(m.ToString(), out var ws)) result.Add(ws);
        }

        return result;
    }
}
