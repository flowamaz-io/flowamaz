using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Analytics aggregation reads over instances/node-states (Process Intelligence + Weather).</summary>
public sealed class WorkflowAnalyticsRepository(FlowAmazDbContext db) : IWorkflowAnalyticsRepository
{
    private static readonly InstanceStatus[] ActiveStatuses =
        [InstanceStatus.Pending, InstanceStatus.Running, InstanceStatus.Waiting, InstanceStatus.Compensating];

    public Task<List<Guid>> GetWorkspacesWithRunsSinceAsync(DateTime since, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AsNoTracking()
            .Where(i => i.CreatedAt >= since)
            .Select(i => i.WorkspaceId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<List<Guid>> GetWorkflowIdsWithRunsSinceAsync(Guid workspaceId, DateTime since, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && i.CreatedAt >= since)
            .Select(i => i.WorkflowDefinitionId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<List<InstanceDurationSample>> GetDurationsAsync(
        Guid workspaceId, Guid workflowDefinitionId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var rows = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId
                        && i.WorkflowDefinitionId == workflowDefinitionId
                        && i.CreatedAt >= from && i.CreatedAt < to)
            .Select(i => new { i.Status, i.StartedAt, i.CompletedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new InstanceDurationSample(
                r.Status,
                r.StartedAt is not null && r.CompletedAt is not null
                    ? (long)(r.CompletedAt.Value - r.StartedAt.Value).TotalMilliseconds
                    : null))
            .ToList();
    }

    public async Task<BottleneckSample?> GetBottleneckAsync(
        Guid workspaceId, Guid workflowDefinitionId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var matchingInstances = db.WorkflowInstances.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId
                        && i.WorkflowDefinitionId == workflowDefinitionId
                        && i.CreatedAt >= from && i.CreatedAt < to);

        var rows = await db.WorkflowNodeStates.AsNoTracking()
            .Where(ns => ns.Status == NodeStatus.Completed && ns.StartedAt != null && ns.CompletedAt != null)
            .Join(matchingInstances, ns => ns.InstanceId, i => i.Id,
                (ns, _) => new { ns.NodeId, ns.StartedAt, ns.CompletedAt })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return null;

        var slowest = rows
            .GroupBy(r => r.NodeId)
            .Select(g => new BottleneckSample(
                g.Key,
                (long)g.Average(r => (r.CompletedAt!.Value - r.StartedAt!.Value).TotalMilliseconds)))
            .OrderByDescending(s => s.AvgMs)
            .First();

        return slowest;
    }

    public Task<int> GetActiveInstanceCountAsync(Guid workspaceId, Guid workflowDefinitionId, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AsNoTracking()
            .CountAsync(i => i.WorkspaceId == workspaceId
                             && i.WorkflowDefinitionId == workflowDefinitionId
                             && ActiveStatuses.Contains(i.Status), cancellationToken);

    public Task<int> GetFailedCountSinceAsync(Guid workspaceId, Guid workflowDefinitionId, DateTime since, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AsNoTracking()
            .CountAsync(i => i.WorkspaceId == workspaceId
                             && i.WorkflowDefinitionId == workflowDefinitionId
                             && i.Status == InstanceStatus.Failed
                             && i.CreatedAt >= since, cancellationToken);
}
