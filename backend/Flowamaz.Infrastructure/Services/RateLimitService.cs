using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis-backed fixed-window rate limiter. Uses INCR + EXPIRE on first hit per window.
/// A sliding window with sorted sets is more accurate but costs more on every check;
/// fixed-window is sufficient for the documented 60/user/hour Co-pilot cap.
/// </summary>
public sealed class RateLimitService : IRateLimitService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RateLimitService> _logger;

    public RateLimitService(IConnectionMultiplexer redis, ILogger<RateLimitService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> CheckAndIncrementAsync(
        string key,
        int maxCount,
        int windowSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        if (windowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("RateLimitService.CheckAndIncrementAsync enter key={Key} max={Max} window={Window}",
            key, maxCount, windowSeconds);

        try
        {
            var db = _redis.GetDatabase();
            var redisKey = (RedisKey)$"ratelimit:{key}";
            var current = await db.StringIncrementAsync(redisKey);
            if (current == 1)
            {
                await db.KeyExpireAsync(redisKey, TimeSpan.FromSeconds(windowSeconds));
            }
            var allowed = current <= maxCount;
            _logger.LogDebug(
                "RateLimitService.CheckAndIncrementAsync exit key={Key} count={Count} allowed={Allowed}",
                key, current, allowed);
            return allowed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "RateLimitService.CheckAndIncrementAsync error key={Key} — failing open (allowing request)", key);
            // Failing open keeps the user-facing path alive when Redis is down; the platform AI cost
            // dashboard still catches anomalies via metering. Failing closed would punish all users
            // on a cache outage, which is worse than allowing brief over-spend.
            return true;
        }
    }
}
