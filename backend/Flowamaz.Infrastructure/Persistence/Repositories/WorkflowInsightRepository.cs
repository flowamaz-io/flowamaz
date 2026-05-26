using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

public sealed class WorkflowInsightRepository(FlowAmazDbContext db) : IWorkflowInsightRepository
{
    public Task<WorkflowInsight?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowInsights.FirstOrDefaultAsync(i => i.Id == id && i.WorkspaceId == workspaceId, cancellationToken);

    public Task<List<WorkflowInsight>> ListAsync(
        Guid workspaceId, InsightSeverity? severity, bool? acknowledged, Guid? workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var query = db.WorkflowInsights.AsNoTracking().Where(i => i.WorkspaceId == workspaceId);
        if (severity is not null) query = query.Where(i => i.Severity == severity);
        if (acknowledged is not null) query = query.Where(i => i.IsAcknowledged == acknowledged);
        if (workflowDefinitionId is not null) query = query.Where(i => i.WorkflowDefinitionId == workflowDefinitionId);
        return query.OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);
    }

    public Task<List<WorkflowInsight>> GetUnacknowledgedForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowInsights.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && !i.IsAcknowledged)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(WorkflowInsight insight, CancellationToken cancellationToken = default) =>
        await db.WorkflowInsights.AddAsync(insight, cancellationToken);

    public void Update(WorkflowInsight insight) => db.WorkflowInsights.Update(insight);

    public async Task ReplaceUnacknowledgedAsync(WorkflowInsight insight, CancellationToken cancellationToken = default)
    {
        var stale = await db.WorkflowInsights
            .Where(i => i.WorkspaceId == insight.WorkspaceId
                        && i.WorkflowDefinitionId == insight.WorkflowDefinitionId
                        && i.InsightType == insight.InsightType
                        && !i.IsAcknowledged)
            .ToListAsync(cancellationToken);

        foreach (var old in stale)
        {
            old.IsDeleted = true;
            old.DeletedAt = DateTime.UtcNow;
            db.WorkflowInsights.Update(old);
        }

        await db.WorkflowInsights.AddAsync(insight, cancellationToken);
    }
}
