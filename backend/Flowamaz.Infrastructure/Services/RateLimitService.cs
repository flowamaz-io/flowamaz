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

    // KEYS = counter keys; ARGV = max1, window1, max2, window2, … Returns the 1-based index of the first full
    // counter (nothing incremented), or 0 after incrementing them all. Runs atomically, so concurrent requests can't
    // both slip into the last slot.
    private const string CheckAndIncrementAllScript = @"
for i = 1, #KEYS do
  local current = tonumber(redis.call('GET', KEYS[i]) or '0')
  if current + 1 > tonumber(ARGV[2 * i - 1]) then return i end
end
for i = 1, #KEYS do
  if redis.call('INCR', KEYS[i]) == 1 then redis.call('EXPIRE', KEYS[i], ARGV[2 * i]) end
end
return 0
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

    public async Task<int> CheckAndIncrementAllAsync(IReadOnlyList<RateLimitCheck> limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.Count == 0) return -1;
        foreach (var l in limits)
        {
            ArgumentException.ThrowIfNullOrEmpty(l.Key);
            if (l.MaxCount <= 0 || l.WindowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(limits), "Each limit needs a positive count and window.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("RateLimitService.CheckAndIncrementAllAsync enter count={Count}", limits.Count);
        try
        {
            // Hash-tagged keys would be needed on Redis Cluster; Flowamaz runs a single Redis (docker-compose).
            var keys = limits.Select(l => (RedisKey)$"ratelimit:{l.Key}").ToArray();
            var args = limits.SelectMany(l => new RedisValue[] { l.MaxCount, l.WindowSeconds }).ToArray();
            var full = (long)await _redis.GetDatabase().ScriptEvaluateAsync(CheckAndIncrementAllScript, keys, args);
            _logger.LogDebug("RateLimitService.CheckAndIncrementAllAsync exit full={Full}", full);
            return full == 0 ? -1 : (int)full - 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RateLimitService.CheckAndIncrementAllAsync error — failing open (allowing request)");
            return -1; // same reasoning as CheckAndIncrementAsync
        }
    }
}
