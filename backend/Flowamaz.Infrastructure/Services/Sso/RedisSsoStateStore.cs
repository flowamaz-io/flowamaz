using System.Security.Cryptography;
using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services.Sso;

/// <summary>
/// Single-use store for SSO sign-ins in progress (OIDC <c>state</c>, SAML <c>RelayState</c>). Each key is 32 random
/// bytes mapped to the sign-in's data (org, return path, SAML request id, OIDC nonce and PKCE verifier) for
/// 10 minutes; <see cref="ConsumeAsync"/> reads and deletes atomically, so a replayed callback fails.
/// </summary>
public sealed class RedisSsoStateStore : ISsoStateStore
{
    private const int TtlSeconds = 600;
    private readonly IConnectionMultiplexer _redis;

    public RedisSsoStateStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<string> CreateAsync(SsoLoginState state, CancellationToken ct = default)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await _redis.GetDatabase().StringSetAsync($"sso:state:{key}", JsonSerializer.Serialize(state), TimeSpan.FromSeconds(TtlSeconds));
        return key;
    }

    public async Task<SsoLoginState?> ConsumeAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) return null;
        var value = await _redis.GetDatabase().StringGetDeleteAsync($"sso:state:{key}");
        if (value.IsNullOrEmpty) return null;
        try { return JsonSerializer.Deserialize<SsoLoginState>(value.ToString()); }
        catch (JsonException) { return null; }
    }
}

/// <summary>SAML assertion ids seen, kept until the assertion would expire anyway (SET NX).</summary>
public sealed class RedisSsoReplayCache : ISsoReplayCache
{
    private readonly IConnectionMultiplexer _redis;

    public RedisSsoReplayCache(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> TryRecordAsync(string orgSlug, string assertionId, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        // Keep a little past expiry to cover clock skew; at least a minute.
        var ttl = expiresAtUtc - DateTime.UtcNow + TimeSpan.FromMinutes(5);
        if (ttl < TimeSpan.FromMinutes(1)) ttl = TimeSpan.FromMinutes(1);
        if (ttl > TimeSpan.FromDays(1)) ttl = TimeSpan.FromDays(1);
        return await _redis.GetDatabase().StringSetAsync($"sso:assertion:{orgSlug}:{assertionId}", "1", ttl, When.NotExists);
    }
}
