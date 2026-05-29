using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

public sealed class WorkflowRoiConfigRepository(FlowAmazDbContext db) : IWorkflowRoiConfigRepository
{
    public Task<WorkflowRoiConfig?> GetForWorkflowAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowRoiConfigs.FirstOrDefaultAsync(
            c => c.WorkflowDefinitionId == workflowDefinitionId && c.WorkspaceId == workspaceId, cancellationToken);

    public Task<List<WorkflowRoiConfig>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowRoiConfigs.AsNoTracking().Where(c => c.WorkspaceId == workspaceId).ToListAsync(cancellationToken);

    public async Task AddAsync(WorkflowRoiConfig config, CancellationToken cancellationToken = default) =>
        await db.WorkflowRoiConfigs.AddAsync(config, cancellationToken);

    public void Update(WorkflowRoiConfig config) => db.WorkflowRoiConfigs.Update(config);
}
