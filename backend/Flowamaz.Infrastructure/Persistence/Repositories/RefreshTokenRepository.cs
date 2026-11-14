using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for refresh tokens. Lookup is by hash; reads are tracked so rotation can revoke
/// the current token and add the replacement within one unit-of-work transaction. The revoke-all methods
/// also end the person's signed-in CLIs (<see cref="Core.Entities.Auth.CliSession"/>), so every
/// "sign this person out everywhere" path covers the terminal too.
/// </summary>
public sealed class RefreshTokenRepository(FlowAmazDbContext db) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default) =>
        await db.RefreshTokens.AddAsync(token, cancellationToken);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task RevokeAllForUserAsync(Guid orgUserId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.OrgUserId == orgUserId && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
        // Signed-in CLIs are sessions too: a password reset, deactivation or SCIM removal ends them as well.
        await db.CliSessions
            .Where(c => c.OrgUserId == orgUserId && c.RevokedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now).SetProperty(c => c.UpdatedAt, now), cancellationToken);
    }

    public async Task<int> RevokeAllForOrgNonOwnersAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var revoked = await db.RefreshTokens
            .Where(t => t.RevokedAt == null && t.ExpiresAt > now
                && db.OrgUsers.Any(u => u.Id == t.OrgUserId && u.OrgId == orgId && !u.IsOrgOwner))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
        // Same for signed-in CLIs (e.g. when the org starts requiring SSO). Counted with the browser sessions.
        revoked += await db.CliSessions
            .Where(c => c.OrgId == orgId && c.RevokedAt == null && c.ExpiresAt > now
                && db.OrgUsers.Any(u => u.Id == c.OrgUserId && !u.IsOrgOwner))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now).SetProperty(c => c.UpdatedAt, now), cancellationToken);
        return revoked;
    }
}
