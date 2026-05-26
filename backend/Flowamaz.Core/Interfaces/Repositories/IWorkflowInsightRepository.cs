using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Process-Intelligence insights. Caller commits via the unit of work.</summary>
public interface IWorkflowInsightRepository
{
    Task<WorkflowInsight?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default);

    Task<List<WorkflowInsight>> ListAsync(
        Guid workspaceId, InsightSeverity? severity, bool? acknowledged, Guid? workflowDefinitionId,
        CancellationToken cancellationToken = default);

    Task<List<WorkflowInsight>> GetUnacknowledgedForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task AddAsync(WorkflowInsight insight, CancellationToken cancellationToken = default);
    void Update(WorkflowInsight insight);

    /// <summary>
    /// Soft-deletes any unacknowledged insight of the same type for the same workflow, then stages
    /// the new one — so the list never accumulates duplicate live insights of a type.
    /// </summary>
    Task ReplaceUnacknowledgedAsync(WorkflowInsight insight, CancellationToken cancellationToken = default);
}
