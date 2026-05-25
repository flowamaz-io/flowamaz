using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis-backed fixed-window rate limiter. INCR and EXPIRE are issued atomically via a Lua
/// script so the first hit cannot miss its TTL (e.g. if the client dies between commands and
/// leaves an immortal counter). A sliding window with sorted sets is more accurate but costs
/// more on every check; fixed-window is sufficient for the documented 60/user/hour Co-pilot cap.
/// </summary>
public sealed class RateLimitService : IRateLimitService
{
    // KEYS[1] = counter key, ARGV[1] = window seconds. Returns the post-increment count.
    // EXPIRE is set only when the key is freshly created (INCR returned 1), so subsequent
    // hits within the same window leave the TTL untouched — that's what makes the window fixed.
    private const string CheckAndIncrementScript = @"
local current = redis.call('INCR', KEYS[1])
if current == 1 then
  redis.call('EXPIRE', KEYS[1], ARGV[1])
end
return current
";

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
            var result = await db.ScriptEvaluateAsync(
                CheckAndIncrementScript,
                keys: [redisKey],
                values: [windowSeconds]);
            var current = (long)result;
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
