using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Read-only aggregation queries over instances/node-states that the Process Intelligence job and
/// Workflow Weather build on. Kept separate from the write repositories — these are analytics reads.
/// </summary>
public interface IWorkflowAnalyticsRepository
{
    Task<List<Guid>> GetWorkspacesWithRunsSinceAsync(DateTime since, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetWorkflowIdsWithRunsSinceAsync(Guid workspaceId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>Status + completed-duration of each instance for a workflow in [from, to).</summary>
    Task<List<InstanceDurationSample>> GetDurationsAsync(Guid workspaceId, Guid workflowDefinitionId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>The node with the highest average completed duration in the window, or null.</summary>
    Task<BottleneckSample?> GetBottleneckAsync(Guid workspaceId, Guid workflowDefinitionId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    Task<int> GetActiveInstanceCountAsync(Guid workspaceId, Guid workflowDefinitionId, CancellationToken cancellationToken = default);
    Task<int> GetFailedCountSinceAsync(Guid workspaceId, Guid workflowDefinitionId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Live count of production (non-test) runs created in the workspace since <paramref name="since"/>.
    /// Read straight from workflow_instances so the dashboard is current without waiting for the
    /// hourly metric roll-up.
    /// </summary>
    Task<int> GetProductionRunCountSinceAsync(Guid workspaceId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>Per-workflow production (non-test) run totals and completed counts for runs created in [from, to).</summary>
    Task<List<WorkflowRunCounts>> GetProductionRunCountsAsync(Guid workspaceId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}

/// <summary>Production run totals for one workflow in a window.</summary>
public sealed record WorkflowRunCounts(Guid WorkflowDefinitionId, int Runs, int Completed);

/// <summary>One instance's status and completed duration (null when not completed).</summary>
public sealed record InstanceDurationSample(InstanceStatus Status, long? DurationMs);

/// <summary>The slowest node in a window.</summary>
public sealed record BottleneckSample(string NodeId, long AvgMs);
