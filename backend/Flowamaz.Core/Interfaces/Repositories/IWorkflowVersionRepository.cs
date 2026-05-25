using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for immutable workflow version snapshots. Add only stages; the caller commits via
/// <see cref="Persistence.IUnitOfWork"/>. Reads are workspace-scoped.
/// </summary>
public interface IWorkflowVersionRepository
{
    Task<WorkflowVersion?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<List<WorkflowVersion>> GetForDefinitionAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkflowVersion?> GetProductionAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowVersion version, CancellationToken cancellationToken = default);
}
