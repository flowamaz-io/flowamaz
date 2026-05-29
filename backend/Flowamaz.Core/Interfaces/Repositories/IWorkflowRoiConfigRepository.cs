using Flowamaz.Core.Entities.Analytics;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Persistence for per-workflow ROI baselines (prompt 05-04). Workspace-scoped.</summary>
public interface IWorkflowRoiConfigRepository
{
    Task<WorkflowRoiConfig?> GetForWorkflowAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<List<WorkflowRoiConfig>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowRoiConfig config, CancellationToken cancellationToken = default);
    void Update(WorkflowRoiConfig config);
}
