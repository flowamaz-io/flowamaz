using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for workspaces and their environments. Org-scoped reads enforce the org boundary;
/// Add* only stage entities and the caller commits via <see cref="Core.Interfaces.Persistence.IUnitOfWork"/>.
/// </summary>
public sealed class WorkspaceRepository(FlowAmazDbContext db) : IWorkspaceRepository
{
    public Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<Workspace?> GetByIdForOrgAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default) =>
        db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id && w.OrgId == orgId, cancellationToken);

    public Task<List<Workspace>> GetForOrgAsync(Guid orgId, CancellationToken cancellationToken = default) =>
        db.Workspaces.AsNoTracking().Where(w => w.OrgId == orgId).ToListAsync(cancellationToken);

    public Task<List<Workspace>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Workspaces.AsNoTracking().ToListAsync(cancellationToken);

    public Task<bool> SlugExistsInOrgAsync(Guid orgId, string slug, CancellationToken cancellationToken = default) =>
        db.Workspaces.AnyAsync(w => w.OrgId == orgId && w.Slug == slug, cancellationToken);

    public async Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
        await db.Workspaces.AddAsync(workspace, cancellationToken);

    public void Update(Workspace workspace) => db.Workspaces.Update(workspace);

    public async Task AddEnvironmentAsync(WorkspaceEnvironment environment, CancellationToken cancellationToken = default) =>
        await db.WorkspaceEnvironments.AddAsync(environment, cancellationToken);

    public Task<List<WorkspaceEnvironment>> GetEnvironmentsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkspaceEnvironments.AsNoTracking().Where(e => e.WorkspaceId == workspaceId).ToListAsync(cancellationToken);

    public Task<WorkspaceEnvironment?> GetEnvironmentByIdAsync(Guid environmentId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkspaceEnvironments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == environmentId && e.WorkspaceId == workspaceId, cancellationToken);
}
