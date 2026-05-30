using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Workspace membership management. Enforces three invariants at the service layer (not just the
/// API): no self-role-change, no self-removal, and never remove/demote the last active Admin.
/// </summary>
public interface IWorkspaceMemberService
{
    Task<WorkspaceMember> AddMemberAsync(
        Guid workspaceId, Guid orgUserId, WorkspaceRole role, Guid addedByUserId, CancellationToken cancellationToken = default);

    Task UpdateRoleAsync(
        Guid workspaceId, Guid targetUserId, WorkspaceRole newRole, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(
        Guid workspaceId, Guid targetUserId, Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current user leaves the workspace. Unlike <see cref="RemoveMemberAsync"/> this permits
    /// self-removal, but still refuses (409) when it would leave the workspace with no active Admin.
    /// </summary>
    Task LeaveWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);

    Task<List<WorkspaceMemberDto>> GetMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Members enriched with email + name — for the members list view.</summary>
    Task<List<WorkspaceMemberDetailDto>> GetDetailedMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceRole?> GetMemberRoleAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken = default);
    Task<bool> IsMemberAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken = default);
}
