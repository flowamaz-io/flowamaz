using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Workflow Twin runs and the run history they simulate from. Every per-workspace query filters on workspace_id.</summary>
public sealed class WorkflowTwinRepository(FlowAmazDbContext db) : IWorkflowTwinRepository
{
    public async Task AddAsync(WorkflowTwinRun run, CancellationToken cancellationToken = default) =>
        await db.WorkflowTwinRuns.AddAsync(run, cancellationToken);

    public async Task PruneAsync(Guid workspaceId, Guid workflowDefinitionId, int keep, CancellationToken cancellationToken = default)
    {
        var stale = await db.WorkflowTwinRuns.AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId && r.WorkflowDefinitionId == workflowDefinitionId)
            .OrderByDescending(r => r.CompletedAt)
            .Skip(keep)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0) return;
        await db.WorkflowTwinRuns
            .Where(r => r.WorkspaceId == workspaceId && stale.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<WorkflowTwinRun?> GetLatestAsync(Guid workspaceId, Guid workflowDefinitionId, CancellationToken cancellationToken = default) =>
        db.WorkflowTwinRuns.AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId && r.WorkflowDefinitionId == workflowDefinitionId)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<List<WorkflowTwinRun>> GetLatestForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var runs = await db.WorkflowTwinRuns.AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId)
            .Select(r => new WorkflowTwinRun
            {
                Id = r.Id,
                WorkspaceId = r.WorkspaceId,
                WorkflowDefinitionId = r.WorkflowDefinitionId,
                VersionLabel = r.VersionLabel,
                Status = r.Status,
                CasesRun = r.CasesRun,
                BreakCount = r.BreakCount,
                WarningCount = r.WarningCount,
                CompletedAt = r.CompletedAt,
            })
            .ToListAsync(cancellationToken);
        return runs.GroupBy(r => r.WorkflowDefinitionId).Select(g => g.MaxBy(r => r.CompletedAt)!).ToList();
    }

    public async Task<List<TwinDueWorkflow>> GetDueAsync(DateTime staleBefore, int limit, CancellationToken cancellationToken = default)
    {
        var due = await db.WorkflowVersions.AsNoTracking()
            .Where(v => v.IsProduction
                && db.WorkflowDefinitions.Any(d => d.Id == v.WorkflowDefinitionId && d.WorkspaceId == v.WorkspaceId))
            .Select(v => new
            {
                v.WorkspaceId,
                v.WorkflowDefinitionId,
                VersionId = v.Id,
                Latest = db.WorkflowTwinRuns
                    .Where(r => r.WorkspaceId == v.WorkspaceId && r.WorkflowDefinitionId == v.WorkflowDefinitionId)
                    .OrderByDescending(r => r.CompletedAt)
                    .Select(r => new { r.WorkflowVersionId, r.CompletedAt })
                    .FirstOrDefault(),
            })
            .Where(x => x.Latest == null || x.Latest.WorkflowVersionId != x.VersionId || x.Latest.CompletedAt < staleBefore)
            .OrderBy(x => x.Latest == null ? DateTime.MinValue : x.Latest.CompletedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return due.Select(x => new TwinDueWorkflow(x.WorkspaceId, x.WorkflowDefinitionId, x.VersionId)).ToList();
    }

    public Task<List<string>> GetRecentPayloadsAsync(Guid workspaceId, Guid workflowDefinitionId, int limit, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && i.WorkflowDefinitionId == workflowDefinitionId && !i.IsTest
                && i.TriggerPayload != null)
            .OrderByDescending(i => i.CreatedAt)
            .Take(limit)
            .Select(i => i.TriggerPayload!)
            .ToListAsync(cancellationToken);

    public async Task<Dictionary<string, string>> GetRecordedOutputsAsync(
        Guid workspaceId, Guid workflowDefinitionId, int runs, CancellationToken cancellationToken = default)
    {
        var ids = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && i.WorkflowDefinitionId == workflowDefinitionId && !i.IsTest
                && (i.Status == InstanceStatus.Completed || i.Status == InstanceStatus.Failed))
            .OrderByDescending(i => i.CreatedAt)
            .Take(runs)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) return [];

        var events = await db.WorkflowEvents.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId && ids.Contains(e.InstanceId) && e.EventType == "NodeCompleted"
                && e.NodeId != null && e.OutputSnapshot != null)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new { NodeId = e.NodeId!, e.OutputSnapshot })
            .Take(runs * 50)
            .ToListAsync(cancellationToken);

        var latest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in events)
            latest.TryAdd(e.NodeId, e.OutputSnapshot!);
        return latest;
    }
}
