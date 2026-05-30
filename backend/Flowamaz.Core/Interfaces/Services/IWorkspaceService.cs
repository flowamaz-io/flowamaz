using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Workspace lifecycle. <see cref="CreateWorkspaceAsync"/> atomically provisions a workspace, its
/// three environments (Dev/Staging/Production) and the creator as Admin.
/// </summary>
public interface IWorkspaceService
{
    Task<Workspace> CreateWorkspaceAsync(
        Guid orgId, string name, string slug, Guid createdByUserId, CancellationToken cancellationToken = default);

    /// <summary>Org-ownership checked: returns null when the workspace belongs to a different org.</summary>
    Task<Workspace?> GetByIdAsync(Guid workspaceId, Guid orgId, CancellationToken cancellationToken = default);

    Task<List<Workspace>> GetForOrgAsync(Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>The workspace's three environments, ordered Dev → Staging → Production.</summary>
    Task<List<WorkspaceEnvironment>> GetEnvironmentsAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task UpdateSettingsAsync(Guid workspaceId, WorkspaceSettings settings, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Sets the workspace to Archived — new triggers are refused; existing data is preserved.</summary>
    Task ArchiveAsync(Guid workspaceId, Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>Returns an Archived workspace to Active.</summary>
    Task RestoreAsync(Guid workspaceId, Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>Returns all active workspace memberships for the given org user, fresh from the DB.</summary>
    Task<List<WorkspaceMembership>> GetUserMembershipsAsync(Guid orgUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-workspace overview cards for every workspace the user belongs to — each enriched with
    /// member count, workflow count, last-active timestamp, status, and the user's role.
    /// </summary>
    Task<List<WorkspaceOverviewItem>> GetUserWorkspaceOverviewAsync(Guid orgUserId, CancellationToken cancellationToken = default);
}
