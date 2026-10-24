using Flowamaz.Core.Interfaces.Services;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis fixed-window rate limiter for the public API (any window; hourly budgets and per-minute plan limits). INCRs a per-bucket counter and
/// sets the hour TTL on first hit; reports remaining budget and the reset epoch for the
/// X-RateLimit-* headers. Fails open (allows the request) if Redis is unavailable.
/// </summary>
public sealed class RedisPublicApiRateLimiter : IPublicApiRateLimiter
{
    private readonly IConnectionMultiplexer _redis;

    public RedisPublicApiRateLimiter(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<PublicApiRateLimit> CheckAsync(string bucket, int max, int windowSeconds, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = (RedisKey)$"public:rl:{bucket}";
            var count = await db.StringIncrementAsync(key);
            if (count == 1)
                await db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds));

            var ttl = await db.KeyTimeToLiveAsync(key) ?? TimeSpan.FromSeconds(windowSeconds);
            var reset = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
            var remaining = Math.Max(0, max - (int)count);
            return new PublicApiRateLimit(count <= max, max, remaining, reset);
        }
        catch
        {
            // Fail open: a Redis outage must not take the public API down.
            var reset = DateTimeOffset.UtcNow.AddSeconds(windowSeconds).ToUnixTimeSeconds();
            return new PublicApiRateLimit(true, max, max, reset);
        }
    }
}
