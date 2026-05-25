using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

public sealed class WorkflowNodeStateRepository(FlowAmazDbContext db) : IWorkflowNodeStateRepository
{
    public Task<List<WorkflowNodeState>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        db.WorkflowNodeStates.Where(s => s.InstanceId == instanceId).ToListAsync(cancellationToken);

    public Task<WorkflowNodeState?> GetByNodeAsync(Guid instanceId, string nodeId, CancellationToken cancellationToken = default) =>
        db.WorkflowNodeStates.FirstOrDefaultAsync(s => s.InstanceId == instanceId && s.NodeId == nodeId, cancellationToken);

    public async Task AddAsync(WorkflowNodeState state, CancellationToken cancellationToken = default) =>
        await db.WorkflowNodeStates.AddAsync(state, cancellationToken);

    public void Update(WorkflowNodeState state) => db.WorkflowNodeStates.Update(state);
}
