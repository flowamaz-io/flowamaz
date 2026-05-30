using Flowamaz.Core.Interfaces.Services;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis fixed-window (1 hour) rate limiter for the public API. INCRs a per-bucket counter and
/// sets the hour TTL on first hit; reports remaining budget and the reset epoch for the
/// X-RateLimit-* headers. Fails open (allows the request) if Redis is unavailable.
/// </summary>
public sealed class RedisPublicApiRateLimiter : IPublicApiRateLimiter
{
    private const int WindowSeconds = 3600;

    private readonly IConnectionMultiplexer _redis;

    public RedisPublicApiRateLimiter(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<PublicApiRateLimit> CheckAsync(string bucket, int maxPerHour, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = (RedisKey)$"public:rl:{bucket}";
            var count = await db.StringIncrementAsync(key);
            if (count == 1)
                await db.KeyExpireAsync(key, TimeSpan.FromSeconds(WindowSeconds));

            var ttl = await db.KeyTimeToLiveAsync(key) ?? TimeSpan.FromSeconds(WindowSeconds);
            var reset = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
            var remaining = Math.Max(0, maxPerHour - (int)count);
            return new PublicApiRateLimit(count <= maxPerHour, maxPerHour, remaining, reset);
        }
        catch
        {
            // Fail open: a Redis outage must not take the public API down.
            var reset = DateTimeOffset.UtcNow.AddSeconds(WindowSeconds).ToUnixTimeSeconds();
            return new PublicApiRateLimit(true, maxPerHour, maxPerHour, reset);
        }
    }
}
