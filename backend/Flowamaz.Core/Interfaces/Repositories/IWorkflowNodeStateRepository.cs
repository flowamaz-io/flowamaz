using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Per-node state snapshots for an instance. Add/Update stage; the caller commits.</summary>
public interface IWorkflowNodeStateRepository
{
    Task<List<WorkflowNodeState>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);
    Task<WorkflowNodeState?> GetByNodeAsync(Guid instanceId, string nodeId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowNodeState state, CancellationToken cancellationToken = default);
    void Update(WorkflowNodeState state);
}
