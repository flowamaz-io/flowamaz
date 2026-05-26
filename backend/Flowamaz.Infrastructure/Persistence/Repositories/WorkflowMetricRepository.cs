using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

public sealed class WorkflowMetricRepository(FlowAmazDbContext db) : IWorkflowMetricRepository
{
    public async Task UpsertAsync(WorkflowMetric metric, CancellationToken cancellationToken = default)
    {
        var existing = await db.WorkflowMetrics.FirstOrDefaultAsync(
            m => m.WorkflowDefinitionId == metric.WorkflowDefinitionId && m.PeriodHour == metric.PeriodHour,
            cancellationToken);

        if (existing is null)
        {
            await db.WorkflowMetrics.AddAsync(metric, cancellationToken);
            return;
        }

        existing.RunsTotal = metric.RunsTotal;
        existing.RunsCompleted = metric.RunsCompleted;
        existing.RunsFailed = metric.RunsFailed;
        existing.RunsCancelled = metric.RunsCancelled;
        existing.AvgDurationMs = metric.AvgDurationMs;
        existing.P95DurationMs = metric.P95DurationMs;
        existing.P99DurationMs = metric.P99DurationMs;
        existing.SlaBreachCount = metric.SlaBreachCount;
        existing.SlaThresholdMs = metric.SlaThresholdMs;
        existing.BottleneckNodeId = metric.BottleneckNodeId;
        existing.BottleneckAvgMs = metric.BottleneckAvgMs;
        db.WorkflowMetrics.Update(existing);
    }

    public Task<List<WorkflowMetric>> GetForDefinitionSinceAsync(Guid workflowDefinitionId, Guid workspaceId, DateTime since, CancellationToken cancellationToken = default) =>
        db.WorkflowMetrics.AsNoTracking()
            .Where(m => m.WorkflowDefinitionId == workflowDefinitionId && m.WorkspaceId == workspaceId && m.PeriodHour >= since)
            .OrderBy(m => m.PeriodHour)
            .ToListAsync(cancellationToken);

    public async Task<List<WorkflowMetric>> GetLatestPerWorkflowAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var rows = await db.WorkflowMetrics.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderByDescending(m => m.PeriodHour)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(m => m.WorkflowDefinitionId)
            .Select(g => g.First())
            .ToList();
    }

    public Task<int> GetRunCountSinceAsync(Guid workspaceId, DateTime since, CancellationToken cancellationToken = default) =>
        db.WorkflowMetrics.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.PeriodHour >= since)
            .SumAsync(m => m.RunsTotal, cancellationToken);
}
