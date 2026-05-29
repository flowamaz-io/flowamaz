using Flowamaz.Core.Entities.Analytics;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Per-hour workflow rollups. Upsert is keyed on (workflow, hour). Caller commits.</summary>
public interface IWorkflowMetricRepository
{
    /// <summary>Insert the metric, or update the existing row for the same (workflow, hour).</summary>
    Task UpsertAsync(WorkflowMetric metric, CancellationToken cancellationToken = default);

    Task<List<WorkflowMetric>> GetForDefinitionSinceAsync(Guid workflowDefinitionId, Guid workspaceId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>Most recent metric row per workflow in the workspace — backs Workflow Weather.</summary>
    Task<List<WorkflowMetric>> GetLatestPerWorkflowAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Sum of RunsTotal across all metric rows since <paramref name="since"/> — for the dashboard.</summary>
    Task<int> GetRunCountSinceAsync(Guid workspaceId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>All metric rows for the workspace whose hour falls in [from, to) — backs ROI aggregation.</summary>
    Task<List<WorkflowMetric>> GetForWorkspaceInRangeAsync(Guid workspaceId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
