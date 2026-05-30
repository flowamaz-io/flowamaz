using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Workspace-scoped persistence for workflow definitions. Add/Update stage only.</summary>
public sealed class WorkflowDefinitionRepository(FlowAmazDbContext db) : IWorkflowDefinitionRepository
{
    public Task<WorkflowDefinition?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Id == id && w.WorkspaceId == workspaceId, cancellationToken);

    public Task<List<WorkflowDefinition>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowDefinitions.AsNoTracking().Where(w => w.WorkspaceId == workspaceId)
            .OrderByDescending(w => w.UpdatedAt).ToListAsync(cancellationToken);

    public Task<WorkflowDefinition?> GetBySlugForWorkspaceAsync(Guid workspaceId, string slug, CancellationToken cancellationToken = default) =>
        db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId && w.Slug == slug, cancellationToken);

    public Task<bool> SlugExistsInWorkspaceAsync(Guid workspaceId, string slug, CancellationToken cancellationToken = default) =>
        db.WorkflowDefinitions.AnyAsync(w => w.WorkspaceId == workspaceId && w.Slug == slug, cancellationToken);

    public async Task AddAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default) =>
        await db.WorkflowDefinitions.AddAsync(definition, cancellationToken);

    public void Update(WorkflowDefinition definition) => db.WorkflowDefinitions.Update(definition);
}
