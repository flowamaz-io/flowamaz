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

    public Task<List<GateDecision>> GetPendingForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.GateDecisions.AsNoTracking()
            .Where(g => g.WorkspaceId == workspaceId && g.Decision == GateDecisionStatus.Pending)
            .OrderBy(g => g.CreatedAt).ToListAsync(cancellationToken);

    public Task<List<GateDecision>> GetExpiredAsync(DateTime asOf, CancellationToken cancellationToken = default) =>
        db.GateDecisions
            .Where(g => g.Decision == GateDecisionStatus.Pending && g.ExpiresAt != null && g.ExpiresAt <= asOf)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(GateDecision gate, CancellationToken cancellationToken = default) =>
        await db.GateDecisions.AddAsync(gate, cancellationToken);

    public void Update(GateDecision gate) => db.GateDecisions.Update(gate);
}
