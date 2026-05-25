using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for refresh tokens. Lookup is by hash; reads are tracked so rotation can revoke
/// the current token and add the replacement within one unit-of-work transaction.
/// </summary>
public sealed class RefreshTokenRepository(FlowAmazDbContext db) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default) =>
        await db.RefreshTokens.AddAsync(token, cancellationToken);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
}
