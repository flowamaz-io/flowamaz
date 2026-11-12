using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Redis-backed <see cref="ITokenRevocationService"/>. Each marker is the revocation time in Unix milliseconds,
/// kept for the access-token lifetime plus a minute of clock slack, after which no token it could reject is
/// still valid. If Redis can't be reached the check lets the request through (and logs it): the token is
/// still signed and unexpired, and the refresh-token revocation already stops it being renewed. Setting a marker
/// never fails the caller either, for the same reason.
/// </summary>
public sealed class RedisTokenRevocationService : ITokenRevocationService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;
    private readonly ILogger<RedisTokenRevocationService> _logger;

    public RedisTokenRevocationService(IConnectionMultiplexer redis, IOptions<JwtOptions> options, ILogger<RedisTokenRevocationService> logger)
    {
        _redis = redis;
        _logger = logger;
        _ttl = TimeSpan.FromMinutes(Math.Max(1, options.Value.AccessTokenExpiryMinutes) + 1);
    }

    internal static string UserKey(Guid orgUserId) => $"auth:revoked:user:{orgUserId:N}";
    internal static string OrgKey(Guid orgId) => $"auth:revoked:org:{orgId:N}";

    public Task RevokeUserAccessAsync(Guid orgUserId, CancellationToken ct = default) =>
        SetMarkerAsync(UserKey(orgUserId), "user", orgUserId);

    public async Task RevokeUsersAccessAsync(IReadOnlyCollection<Guid> orgUserIds, CancellationToken ct = default)
    {
        if (orgUserIds.Count == 0) return;
        _logger.LogInformation("RedisTokenRevocationService.RevokeUsersAccessAsync enter count={Count}", orgUserIds.Count);
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var batch = _redis.GetDatabase().CreateBatch();
            var writes = orgUserIds.Distinct().Select(id => batch.StringSetAsync(UserKey(id), now, _ttl)).ToList();
            batch.Execute();
            await Task.WhenAll(writes);
            _logger.LogInformation("RedisTokenRevocationService.RevokeUsersAccessAsync exit count={Count}", writes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RedisTokenRevocationService.RevokeUsersAccessAsync error count={Count}", orgUserIds.Count);
        }
    }

    public Task RevokeOrgNonOwnerAccessAsync(Guid orgId, CancellationToken ct = default) =>
        SetMarkerAsync(OrgKey(orgId), "org", orgId);

    public async Task<bool> IsRevokedAsync(Guid orgUserId, Guid orgId, bool isOrgOwner, long issuedAtUnixMs, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            // One GET per key, not MGET: the two keys may live on different Redis Cluster slots.
            var lookups = new List<Task<RedisValue>> { db.StringGetAsync(UserKey(orgUserId)) };
            if (!isOrgOwner) lookups.Add(db.StringGetAsync(OrgKey(orgId)));
            foreach (var value in await Task.WhenAll(lookups))
            {
                if (value.HasValue && long.TryParse(value.ToString(), out var revokedAt) && issuedAtUnixMs < revokedAt)
                    return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "RedisTokenRevocationService.IsRevokedAsync error orgUserId={UserId} — allowing the request", orgUserId);
            return false;
        }
    }

    private async Task SetMarkerAsync(string key, string scope, Guid id)
    {
        _logger.LogInformation("RedisTokenRevocationService.Revoke enter scope={Scope} id={Id}", scope, id);
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _redis.GetDatabase().StringSetAsync(key, now, _ttl);
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
