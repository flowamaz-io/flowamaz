using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for signed-in CLIs. Every per-person query filters on org_user_id.</summary>
public sealed class CliSessionRepository(FlowAmazDbContext db) : ICliSessionRepository
{
    public async Task AddAsync(CliSession session, CancellationToken cancellationToken = default) =>
        await db.CliSessions.AddAsync(session, cancellationToken);

    public Task<CliSession?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        db.CliSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);

    public Task<CliSession?> GetForUserAsync(Guid orgUserId, Guid sessionId, CancellationToken cancellationToken = default) =>
        db.CliSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.OrgUserId == orgUserId, cancellationToken);

    public async Task<IReadOnlyList<CliSession>> ListActiveForUserAsync(Guid orgUserId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await db.CliSessions.AsNoTracking()
            .Where(s => s.OrgUserId == orgUserId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt ?? s.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
