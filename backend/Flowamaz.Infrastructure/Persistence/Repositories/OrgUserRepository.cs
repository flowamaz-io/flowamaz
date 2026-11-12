using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for org users. Reads are tracked so callers (login bookkeeping in
/// OrgUserService) can mutate the returned entity and commit via the unit of work.
/// </summary>
public sealed class OrgUserRepository(FlowAmazDbContext db) : IOrgUserRepository
{
    public Task<OrgUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.OrgUsers.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<OrgUser?> GetByEmailAndOrgAsync(string email, Guid orgId, CancellationToken cancellationToken = default) =>
        db.OrgUsers.FirstOrDefaultAsync(u => u.OrgId == orgId && u.Email == email, cancellationToken);

    public async Task AddAsync(OrgUser orgUser, CancellationToken cancellationToken = default) =>
        await db.OrgUsers.AddAsync(orgUser, cancellationToken);

    public async Task<(List<OrgUser> Users, int Total)> GetSsoLinkedAsync(
        Guid orgId, string? search, int take, CancellationToken cancellationToken = default)
    {
        var query = db.OrgUsers.AsNoTracking().Where(u => u.OrgId == orgId && u.SsoSubject != null && u.ScimDeletedAt == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(u => EF.Functions.ILike(u.Email, pattern, "\\") || EF.Functions.ILike(u.Name, pattern, "\\"));
        }
        var total = await query.CountAsync(cancellationToken);
        var users = await query.OrderBy(u => u.Email).Take(take).ToListAsync(cancellationToken);
        return (users, total);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
