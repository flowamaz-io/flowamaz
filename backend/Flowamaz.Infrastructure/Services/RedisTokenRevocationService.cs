using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis-backed <see cref="ITokenRevocationService"/>.
/// <list type="bullet">
/// <item><b>One clock.</b> Markers are written with Redis's own time (a Lua script calls <c>TIME</c>), and tokens are
/// stamped with this server's clock corrected to Redis's (the offset is re-measured every minute). Servers whose clocks
/// disagree therefore still compare issue times and markers correctly.</item>
/// <item><b>Markers</b> (per person, and per organisation for non-owners) hold the revocation time in Unix ms. A token
/// issued at or before it is refused, so one issued in the same millisecond, possibly with roles read before the change,
/// doesn't slip through. They are kept for the access-token lifetime plus a minute, after which no token they could
/// refuse is still valid.</item>
/// <item><b>Single tokens</b> (sign-out) are refused by their jti until they would have expired.</item>
/// </list>
/// If Redis can't be reached the check lets the request through (and logs it): the token is still signed and unexpired,
/// and the refresh-token revocation already stops it being renewed. Setting a marker never fails the caller either.
/// </summary>
public sealed class RedisTokenRevocationService : ITokenRevocationService
{
    // Sets KEYS[1] to Redis's current time in ms for ARGV[1] ms; returns that time. One key per call (Redis Cluster).
    private const string MarkScript = """
        local t = redis.call('TIME')
        local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)
        redis.call('SET', KEYS[1], now, 'PX', ARGV[1])
        return now
        """;

    private static readonly TimeSpan ClockResync = TimeSpan.FromMinutes(1);

    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;
    private readonly ILogger<RedisTokenRevocationService> _logger;
    private readonly Func<long> _localNowMs;
    private long _offsetMs;            // Redis time − local time
    private long _offsetMeasuredAtMs = long.MinValue;
    private long _lastMarkerMs;        // newest marker this server wrote; tokens it issues afterwards are stamped later

    public RedisTokenRevocationService(IConnectionMultiplexer redis, IOptions<JwtOptions> options, ILogger<RedisTokenRevocationService> logger)
        : this(redis, options, logger, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { }

    internal RedisTokenRevocationService(
        IConnectionMultiplexer redis, IOptions<JwtOptions> options, ILogger<RedisTokenRevocationService> logger, Func<long> localNowMs)
    {
        _redis = redis;
        _logger = logger;
        _localNowMs = localNowMs;
        _ttl = TimeSpan.FromMinutes(Math.Max(1, options.Value.AccessTokenExpiryMinutes) + 1);
    }

    internal static string UserKey(Guid orgUserId) => $"auth:revoked:user:{orgUserId:N}";
    internal static string OrgKey(Guid orgId) => $"auth:revoked:org:{orgId:N}";
    internal static string TokenKey(string tokenId) => $"auth:revoked:jti:{tokenId}";

    public Task RevokeUserAccessAsync(Guid orgUserId, CancellationToken ct = default) =>
        MarkAsync([UserKey(orgUserId)], "user", orgUserId.ToString());

    public Task RevokeUsersAccessAsync(IReadOnlyCollection<Guid> orgUserIds, CancellationToken ct = default) =>
        orgUserIds.Count == 0
            ? Task.CompletedTask
            : MarkAsync(orgUserIds.Distinct().Select(id => (RedisKey)UserKey(id)).ToArray(), "users", orgUserIds.Count.ToString());

    public Task RevokeOrgNonOwnerAccessAsync(Guid orgId, CancellationToken ct = default) =>
        MarkAsync([OrgKey(orgId)], "org", orgId.ToString());

    public async Task RevokeTokenAsync(string tokenId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenId)) return;
        var left = expiresAt - DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1); // + clock skew allowed by validation
        if (left <= TimeSpan.Zero) return;
        _logger.LogInformation("RedisTokenRevocationService.RevokeTokenAsync enter");
        try
        {
            await _redis.GetDatabase().StringSetAsync(TokenKey(tokenId), 1, left < _ttl ? left : _ttl);
            _logger.LogInformation("RedisTokenRevocationService.RevokeTokenAsync exit");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RedisTokenRevocationService.RevokeTokenAsync error — the token runs out on its own");
        }
    }

    public async Task<bool> IsRevokedAsync(
        Guid orgUserId, Guid orgId, bool isOrgOwner, long issuedAtUnixMs, string? tokenId = null, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            // One GET per key, not MGET: the keys may live on different Redis Cluster slots.
            var markers = new List<Task<RedisValue>> { db.StringGetAsync(UserKey(orgUserId)) };
            if (!isOrgOwner) markers.Add(db.StringGetAsync(OrgKey(orgId)));
            var single = string.IsNullOrWhiteSpace(tokenId) ? null : db.KeyExistsAsync(TokenKey(tokenId));

            foreach (var value in await Task.WhenAll(markers))
            {
                if (value.HasValue && long.TryParse(value.ToString(), out var revokedAt) && issuedAtUnixMs <= revokedAt)
                    return true;
            }
            return single is not null && await single;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "RedisTokenRevocationService.IsRevokedAsync error orgUserId={UserId} — allowing the request", orgUserId);
            return false;
        }
    }

    public long IssueTimeUnixMs()
    {
        var local = _localNowMs();
        var measuredAt = Interlocked.Read(ref _offsetMeasuredAtMs);
        if (measuredAt == long.MinValue || local - measuredAt >= (long)ClockResync.TotalMilliseconds) MeasureOffset(local);
        // A token issued here after this server wrote a marker (e.g. an SSO sign-in that changed roles) must never
        // compare as "before" it, whatever the offset's small measuring error.
        return Math.Max(local + Interlocked.Read(ref _offsetMs), Interlocked.Read(ref _lastMarkerMs) + 1);
    }

    private void MeasureOffset(long localBefore)
    {
        try
        {
            var server = _redis.GetDatabase().Execute("TIME");
            var localAfter = _localNowMs();
            var parts = (RedisResult[])server!;
            var redisMs = (long)parts[0] * 1000 + (long)parts[1] / 1000;
            Interlocked.Exchange(ref _offsetMs, redisMs - (localBefore + localAfter) / 2);
            Interlocked.Exchange(ref _offsetMeasuredAtMs, localAfter);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // Keep the last offset (0 if never measured) and try again on the next token.
            _logger.LogWarning(ex, "RedisTokenRevocationService.IssueTimeUnixMs couldn't read Redis time — using the local clock");
        }
    }

    private async Task MarkAsync(RedisKey[] keys, string scope, string id)
    {
        _logger.LogInformation("RedisTokenRevocationService.Revoke enter scope={Scope} id={Id}", scope, id);
        try
        {
            var db = _redis.GetDatabase();
            var ttlMs = (long)_ttl.TotalMilliseconds;
            var results = await Task.WhenAll(keys.Select(k => db.ScriptEvaluateAsync(MarkScript, [k], [ttlMs])));
            var newest = results.Max(r => (long)r);
            long seen;
            while ((seen = Interlocked.Read(ref _lastMarkerMs)) < newest &&
                   Interlocked.CompareExchange(ref _lastMarkerMs, newest, seen) != seen) { }
            _logger.LogInformation("RedisTokenRevocationService.Revoke exit scope={Scope} id={Id}", scope, id);
        }
        catch (Exception ex)
        {
            // The change that called this is already saved and its refresh tokens are revoked; failing the request
            // now would only hide that. Current access tokens then run out on their own (at most one lifetime).
            _logger.LogError(ex, "RedisTokenRevocationService.Revoke error scope={Scope} id={Id}", scope, id);
        }
    }
}
