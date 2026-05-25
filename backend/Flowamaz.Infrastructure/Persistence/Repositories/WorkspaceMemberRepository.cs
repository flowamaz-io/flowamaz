using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for workspace members. Every query is filtered by workspace_id so a member of one
/// workspace can never be read or mutated through another (CLAUDE.md critical rule 1).
/// Single-member reads are tracked so callers can mutate and commit via the unit of work.
/// </summary>
public sealed class WorkspaceMemberRepository(FlowAmazDbContext db) : IWorkspaceMemberRepository
{
    public Task<WorkspaceMember?> GetAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken = default) =>
        db.WorkspaceMembers.FirstOrDefaultAsync(
            m => m.WorkspaceId == workspaceId && m.OrgUserId == orgUserId, cancellationToken);

    public Task<List<WorkspaceMember>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkspaceMembers.AsNoTracking().Where(m => m.WorkspaceId == workspaceId).ToListAsync(cancellationToken);

    public async Task<List<WorkspaceMembership>> GetActiveMembershipsForUserAsync(Guid orgUserId, CancellationToken cancellationToken = default)
    {
        // Project Role client-side: the column is stored as text, so a SQL "(int)Role" cast would
        // try to parse e.g. "Viewer" as an integer and fail (Postgres 22P02).
        var rows = await (
            from m in db.WorkspaceMembers.AsNoTracking()
            where m.OrgUserId == orgUserId && m.IsActive
            join w in db.Workspaces.AsNoTracking() on m.WorkspaceId equals w.Id
            select new { w.Id, w.Slug, w.Name, m.Role })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new WorkspaceMembership(r.Id, r.Slug, r.Name, r.Role.ToString(), (int)r.Role))
            .ToList();
    }

    public Task<List<WorkspaceMemberDetailDto>> GetDetailedMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        (from m in db.WorkspaceMembers.AsNoTracking()
         where m.WorkspaceId == workspaceId
         join u in db.OrgUsers.AsNoTracking() on m.OrgUserId equals u.Id
         select new WorkspaceMemberDetailDto(m.OrgUserId, u.Email, u.Name, m.Role, m.JoinedAt, m.IsActive))
        .ToListAsync(cancellationToken);

    public Task<int> CountActiveAdminsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkspaceMembers.CountAsync(
            m => m.WorkspaceId == workspaceId && m.IsActive && m.Role == WorkspaceRole.Admin, cancellationToken);

    public async Task AddAsync(WorkspaceMember member, CancellationToken cancellationToken = default) =>
        await db.WorkspaceMembers.AddAsync(member, cancellationToken);

    public void Update(WorkspaceMember member) => db.WorkspaceMembers.Update(member);

    public void Remove(WorkspaceMember member) => db.WorkspaceMembers.Remove(member);
}
