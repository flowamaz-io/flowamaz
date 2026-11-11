using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository(FlowAmazDbContext db) : IPasswordResetTokenRepository
{
    public async Task AddAsync(PasswordResetToken token, CancellationToken ct = default) =>
        await db.PasswordResetTokens.AddAsync(token, ct);

    public Task<PasswordResetToken?> GetByHashAsync(string hash, CancellationToken ct = default) =>
        db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

    public Task InvalidateAllForUserAsync(Guid orgUserId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db.PasswordResetTokens.Where(t => t.OrgUserId == orgUserId && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
    }
}
