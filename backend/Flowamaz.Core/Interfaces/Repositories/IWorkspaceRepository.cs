using Flowamaz.Core.Entities.Workspaces;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for workspaces and their environments. Add* only stage entities; the caller
/// commits via <see cref="Persistence.IUnitOfWork"/>. Lookups that cross the org boundary use the
/// org-scoped overloads so callers cannot read another org's workspace.
/// </summary>
public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns the workspace only when it belongs to <paramref name="orgId"/>; otherwise null.</summary>
    Task<Workspace?> GetByIdForOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);

    Task<List<Workspace>> GetForOrgAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsInOrgAsync(Guid orgId, string slug, CancellationToken cancellationToken = default);

    Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default);
    void Update(Workspace workspace);

    Task AddEnvironmentAsync(WorkspaceEnvironment environment, CancellationToken cancellationToken = default);
    Task<List<WorkspaceEnvironment>> GetEnvironmentsAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceEnvironment?> GetEnvironmentByIdAsync(Guid environmentId, Guid workspaceId, CancellationToken cancellationToken = default);
}
