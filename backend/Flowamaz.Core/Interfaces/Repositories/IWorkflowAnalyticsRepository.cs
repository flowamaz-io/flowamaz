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
}

/// <summary>One instance's status and completed duration (null when not completed).</summary>
public sealed record InstanceDurationSample(InstanceStatus Status, long? DurationMs);

/// <summary>The slowest node in a window.</summary>
public sealed record BottleneckSample(string NodeId, long AvgMs);
