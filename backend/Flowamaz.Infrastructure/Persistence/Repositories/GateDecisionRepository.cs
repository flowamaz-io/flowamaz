using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Workspace-scoped persistence for human approval gates. Add/Update stage only.</summary>
public sealed class GateDecisionRepository(FlowAmazDbContext db) : IGateDecisionRepository
{
    public Task<GateDecision?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.GateDecisions.FirstOrDefaultAsync(g => g.Id == id && g.WorkspaceId == workspaceId, cancellationToken);

    public Task<GateDecision?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.GateDecisions.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<GateDecision?> GetByNodeAsync(Guid instanceId, string nodeId, CancellationToken cancellationToken = default) =>
        db.GateDecisions.FirstOrDefaultAsync(g => g.InstanceId == instanceId && g.NodeId == nodeId, cancellationToken);

    public Task<List<GateDecision>> GetForInstanceAsync(Guid instanceId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.GateDecisions.AsNoTracking()
            .Where(g => g.InstanceId == instanceId && g.WorkspaceId == workspaceId)
            .OrderBy(g => g.CreatedAt).ToListAsync(cancellationToken);

    public Task<List<GateDecision>> GetPendingForWorkspaceAsync(Guid workspaceId, bool includeTest = false, CancellationToken cancellationToken = default) =>
        db.GateDecisions.AsNoTracking()
            .Where(g => g.WorkspaceId == workspaceId && g.Decision == GateDecisionStatus.Pending)
            // A cancelled/failed/finished run's approval no longer needs anyone's attention; test-run
            // gates stay out of the production queue unless asked for (O2).
            .Where(g => db.WorkflowInstances.Any(i => i.Id == g.InstanceId && i.WorkspaceId == workspaceId
                && (includeTest || !i.IsTest)
                && (i.Status == InstanceStatus.Waiting || i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Pending)))
            .OrderBy(g => g.CreatedAt).ToListAsync(cancellationToken);

    public Task<List<GateDecision>> GetRecentForWorkspaceAsync(Guid workspaceId, bool includeTest, int take, CancellationToken cancellationToken = default) =>
        db.GateDecisions.AsNoTracking()
            .Where(g => g.WorkspaceId == workspaceId)
            .Where(g => db.WorkflowInstances.Any(i => i.Id == g.InstanceId && i.WorkspaceId == workspaceId
                && (includeTest || !i.IsTest)
                && (g.Decision != GateDecisionStatus.Pending
                    || i.Status == InstanceStatus.Waiting || i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Pending)))
            .OrderByDescending(g => g.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<HashSet<Guid>> GetTestInstanceIdsAsync(
        Guid workspaceId, IReadOnlyCollection<Guid> instanceIds, CancellationToken cancellationToken = default)
    {
        if (instanceIds.Count == 0) return [];
        var ids = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && i.IsTest && instanceIds.Contains(i.Id))
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public Task<List<GateDecision>> GetExpiredAsync(DateTime asOf, CancellationToken cancellationToken = default) =>
        db.GateDecisions
            .Where(g => g.Decision == GateDecisionStatus.Pending && g.ExpiresAt != null && g.ExpiresAt <= asOf)
            .Where(g => db.WorkflowInstances.Any(i => i.Id == g.InstanceId
                && (i.Status == InstanceStatus.Waiting || i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Pending)))
            .ToListAsync(cancellationToken);

    public Task<bool> IsInstanceFinishedAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AnyAsync(i => i.Id == instanceId
            && (i.Status == InstanceStatus.Cancelled || i.Status == InstanceStatus.Completed || i.Status == InstanceStatus.Failed),
            cancellationToken);

    public async Task AddAsync(GateDecision gate, CancellationToken cancellationToken = default) =>
        await db.GateDecisions.AddAsync(gate, cancellationToken);

    public void Update(GateDecision gate) => db.GateDecisions.Update(gate);
}
