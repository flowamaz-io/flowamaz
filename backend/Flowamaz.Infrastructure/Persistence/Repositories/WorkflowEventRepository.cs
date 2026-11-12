using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Append-only access to the event log. Insert and read only — there is intentionally no update or
/// delete path so the audit trail stays immutable (prompt 02-01). The caller commits the staged
/// insert via the unit of work.
/// </summary>
public sealed class WorkflowEventRepository(FlowAmazDbContext db) : IWorkflowEventRepository
{
    public async Task AppendAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default) =>
        await db.WorkflowEvents.AddAsync(workflowEvent, cancellationToken);

    public async Task<List<Core.Models.EmpathyRunTimeline>> GetRunTimelinesAsync(
        Guid workspaceId, Guid workflowDefinitionId, DateTime since, int maxRuns, CancellationToken cancellationToken = default)
    {
        var runs = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && i.WorkflowDefinitionId == workflowDefinitionId && !i.IsTest
                && i.StartedAt != null && i.StartedAt >= since
                && (i.Status == Core.Enums.InstanceStatus.Completed || i.Status == Core.Enums.InstanceStatus.Failed))
            .OrderByDescending(i => i.StartedAt)
            .Take(maxRuns)
            .Select(i => new { i.Id, StartedAt = i.StartedAt!.Value, EndedAt = i.CompletedAt ?? i.FailedAt })
            .ToListAsync(cancellationToken);
        if (runs.Count == 0) return [];

        var ids = runs.Select(r => r.Id).ToList();
        var completed = await db.WorkflowEvents.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId && ids.Contains(e.InstanceId) && e.EventType == "NodeCompleted" && e.NodeId != null)
            .Select(e => new { e.InstanceId, NodeId = e.NodeId!, e.OccurredAt })
            .ToListAsync(cancellationToken);
        var gates = await db.GateDecisions.AsNoTracking()
            .Where(g => g.WorkspaceId == workspaceId && ids.Contains(g.InstanceId))
            .Select(g => new { g.InstanceId, g.NodeId, g.CreatedAt, g.DecidedAt })
            .ToListAsync(cancellationToken);

        var nodesByRun = completed.ToLookup(c => c.InstanceId);
        var gatesByRun = gates.ToLookup(g => g.InstanceId);
        return runs
            .Where(r => r.EndedAt is not null)
            .Select(r => new Core.Models.EmpathyRunTimeline(
                r.Id, r.StartedAt, r.EndedAt!.Value,
                nodesByRun[r.Id].OrderBy(c => c.OccurredAt).Select(c => (c.NodeId, c.OccurredAt)).ToList(),
                gatesByRun[r.Id].OrderBy(g => g.CreatedAt).Select(g => (g.NodeId, g.CreatedAt, g.DecidedAt)).ToList()))
            .ToList();
    }

    public Task<List<WorkflowEvent>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        db.WorkflowEvents.AsNoTracking()
            .Where(e => e.InstanceId == instanceId)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(cancellationToken);

    public async Task<long> GetNextSequenceNumberAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var max = await db.WorkflowEvents
            .Where(e => e.InstanceId == instanceId)
            .Select(e => (long?)e.SequenceNumber)
            .MaxAsync(cancellationToken);
        return (max ?? 0) + 1;
    }
}
