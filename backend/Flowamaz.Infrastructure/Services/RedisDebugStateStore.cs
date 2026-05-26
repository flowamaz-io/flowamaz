using Flowamaz.Core.Interfaces.Services;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis-backed step-debugger state with a 1-hour TTL (debug sessions expire on their own). Keys:
/// <c>fmz:debug:{id}:pause_after</c>, <c>fmz:debug:{id}:step</c>, <c>fmz:debug:{id}:branch:{router}</c>.
/// </summary>
public sealed class RedisDebugStateStore : IDebugStateStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private readonly IConnectionMultiplexer _redis;

    public RedisDebugStateStore(IConnectionMultiplexer redis) => _redis = redis;

    private static RedisKey PauseKey(Guid id) => $"fmz:debug:{id}:pause_after";
    private static RedisKey StepKey(Guid id) => $"fmz:debug:{id}:step";
    private static RedisKey BranchKey(Guid id, string router) => $"fmz:debug:{id}:branch:{router}";

    public Task SetPauseAfterAsync(Guid instanceId, string nodeId, CancellationToken cancellationToken = default) =>
        _redis.GetDatabase().StringSetAsync(PauseKey(instanceId), nodeId, Ttl);

    public async Task<string?> GetPauseAfterAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(PauseKey(instanceId));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public Task GrantStepAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        _redis.GetDatabase().StringSetAsync(StepKey(instanceId), "1", Ttl);

    public async Task<bool> ConsumeStepAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase().StringGetDeleteAsync(StepKey(instanceId));
        return !value.IsNullOrEmpty;
    }

    public Task SetForcedBranchAsync(Guid instanceId, string routerNodeId, string edgeId, CancellationToken cancellationToken = default) =>
        _redis.GetDatabase().StringSetAsync(BranchKey(instanceId, routerNodeId), edgeId, Ttl);

    public async Task<string?> GetForcedBranchAsync(Guid instanceId, string routerNodeId, CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(BranchKey(instanceId, routerNodeId));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public Task ClearAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        // Pause + step are cleared explicitly; forced branches expire on their TTL.
        return db.KeyDeleteAsync([PauseKey(instanceId), StepKey(instanceId)]);
    }
}
