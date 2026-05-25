using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
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

    public Task<int> CountActiveAdminsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkspaceMembers.CountAsync(
            m => m.WorkspaceId == workspaceId && m.IsActive && m.Role == WorkspaceRole.Admin, cancellationToken);

    public async Task AddAsync(WorkspaceMember member, CancellationToken cancellationToken = default) =>
        await db.WorkspaceMembers.AddAsync(member, cancellationToken);

    public void Update(WorkspaceMember member) => db.WorkspaceMembers.Update(member);

    public void Remove(WorkspaceMember member) => db.WorkspaceMembers.Remove(member);
}
