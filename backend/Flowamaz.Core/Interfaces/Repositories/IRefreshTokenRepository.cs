using Flowamaz.Core.Entities.Auth;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for refresh tokens. Lookup is by hash only — the plain token is never stored.
/// Reads are tracked so rotation (revoke + issue) can mutate and commit in one transaction.
/// </summary>
public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeAllForUserAsync(Guid orgUserId, CancellationToken cancellationToken = default);

    /// <summary>Revokes the live sessions of every non-owner in the org (used when an org starts requiring SSO).</summary>
    Task<int> RevokeAllForOrgNonOwnersAsync(Guid orgId, CancellationToken cancellationToken = default);
}
