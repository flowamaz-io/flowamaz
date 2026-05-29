namespace Flowamaz.Application.Analytics;

/// <summary>
/// Deterministic ROI analytics (prompt 05-04): combines admin-set <c>WorkflowRoiConfig</c> baselines
/// with actual run counts from <c>WorkflowMetric</c>. No AI, no estimation.
/// </summary>
public interface IRoiAnalyticsService
{
    Task<WorkspaceRoiSummary> GetWorkspaceRoiAsync(Guid workspaceId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default);

    Task<WorkflowRoiDetail?> GetWorkflowRoiAsync(Guid workspaceId, Guid workflowId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default);

    Task<RoiConfigDto?> GetConfigAsync(Guid workspaceId, Guid workflowId, CancellationToken ct = default);

    /// <summary>Creates or updates the workflow's ROI baseline. Returns null if the workflow is not in the workspace.</summary>
    Task<RoiConfigDto?> UpsertConfigAsync(Guid workspaceId, Guid workflowId, RoiConfigDto config, CancellationToken ct = default);
}
