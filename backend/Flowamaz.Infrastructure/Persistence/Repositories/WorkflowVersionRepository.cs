using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Workspace-scoped persistence for immutable workflow version snapshots.</summary>
public sealed class WorkflowVersionRepository(FlowAmazDbContext db) : IWorkflowVersionRepository
{
    public Task<WorkflowVersion?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowVersions.FirstOrDefaultAsync(v => v.Id == id && v.WorkspaceId == workspaceId, cancellationToken);

    public Task<List<WorkflowVersion>> GetForDefinitionAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowVersions.AsNoTracking()
            .Where(v => v.WorkflowDefinitionId == workflowDefinitionId && v.WorkspaceId == workspaceId)
            .OrderByDescending(v => v.CreatedAt).ToListAsync(cancellationToken);

    public Task<WorkflowVersion?> GetProductionAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(
            v => v.WorkflowDefinitionId == workflowDefinitionId && v.WorkspaceId == workspaceId && v.IsProduction,
            cancellationToken);

    public Task<WorkflowVersion?> GetLatestAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowVersions.AsNoTracking()
            .Where(v => v.WorkflowDefinitionId == workflowDefinitionId && v.WorkspaceId == workspaceId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(WorkflowVersion version, CancellationToken cancellationToken = default) =>
        await db.WorkflowVersions.AddAsync(version, cancellationToken);

    public void Update(WorkflowVersion version) => db.WorkflowVersions.Update(version);
}
