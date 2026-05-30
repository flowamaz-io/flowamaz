using System.Security.Cryptography;
using Flowamaz.Core.Interfaces.Services;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services.Sso;

/// <summary>
/// Single-use OIDC <c>state</c> store (CSRF defence). Each state is a 32-byte random token mapped
/// to the org slug with a 10-minute TTL; <see cref="ConsumeAsync"/> atomically reads and deletes it,
/// so a replayed state fails.
/// </summary>
public sealed class RedisOidcStateStore : IOidcStateStore
{
    private const int TtlSeconds = 600;

    private readonly IConnectionMultiplexer _redis;

    public RedisOidcStateStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<string> CreateAsync(string orgSlug, CancellationToken ct = default)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await _redis.GetDatabase().StringSetAsync($"oidc:state:{state}", orgSlug, TimeSpan.FromSeconds(TtlSeconds));
        return state;
    }

    public async Task<string?> ConsumeAsync(string state, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var db = _redis.GetDatabase();
        var key = (RedisKey)$"oidc:state:{state}";
        var value = await db.StringGetDeleteAsync(key);
        return value.IsNullOrEmpty ? null : value.ToString();
    }
}
