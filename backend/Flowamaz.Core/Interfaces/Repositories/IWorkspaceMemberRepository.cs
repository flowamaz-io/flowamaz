using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for workspace members. Every read is scoped by workspace_id (workspace isolation,
/// CLAUDE.md critical rule 1). The caller commits via <see cref="Persistence.IUnitOfWork"/>.
/// </summary>
public interface IWorkspaceMemberRepository
{
    Task<WorkspaceMember?> GetAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken = default);
    Task<List<WorkspaceMember>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>A user's active memberships across all their workspaces (joined to workspace slug/name) — for JWT claims and /me.</summary>
    Task<List<WorkspaceMembership>> GetActiveMembershipsForUserAsync(Guid orgUserId, CancellationToken cancellationToken = default);

    /// <summary>Workspace members joined to their org user (email + name) — for the members list view.</summary>
    Task<List<WorkspaceMemberDetailDto>> GetDetailedMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Count of active Admins in the workspace — used to guard the last-admin rule.</summary>
    Task<int> CountActiveAdminsAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task AddAsync(WorkspaceMember member, CancellationToken cancellationToken = default);
    void Update(WorkspaceMember member);
    void Remove(WorkspaceMember member);
}
