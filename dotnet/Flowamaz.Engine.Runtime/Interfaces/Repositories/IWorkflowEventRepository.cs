using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Append-only access to the instance event log. There is deliberately NO update or delete
/// method — the log is immutable (prompt 02-01). <see cref="AppendAsync"/> stages an insert; the
/// caller assigns the sequence number from <see cref="GetNextSequenceNumberAsync"/> and commits.
/// </summary>
public interface IWorkflowEventRepository
{
    Task AppendAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finished, non-test runs of a workflow in this workspace since <paramref name="since"/> (newest first, at most
    /// <paramref name="maxRuns"/>), with their completed nodes and approval steps, for the empathy analysis.
    /// </summary>
    /// <summary>
    /// Finished, non-test runs per workflow since <paramref name="since"/>, straight from the runs (not the hourly
    /// metrics rollup), so a comparison agrees with what the run history shows right after the runs finish. A run over
    /// its workflow's SLA threshold counts as a breach.
    /// </summary>
    Task<Dictionary<Guid, Models.RunPerformanceRow>> GetRunPerformanceAsync(
        Guid workspaceId, IReadOnlyCollection<Guid> workflowDefinitionIds, DateTime since, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Dictionary<Guid, Models.RunPerformanceRow>());

    /// <param name="nodeIds">Only completions of these nodes are loaded (e.g. the steps that send messages), so a run
    /// with loops can't return thousands of rows.</param>
    Task<List<Models.EmpathyRunTimeline>> GetRunTimelinesAsync(
        Guid workspaceId, Guid workflowDefinitionId, DateTime since, int maxRuns, IReadOnlyCollection<string> nodeIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<Models.EmpathyRunTimeline>());
    Task<List<WorkflowEvent>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Next sequence number for the instance — (max persisted) + 1, or 1 for the first event.</summary>
    Task<long> GetNextSequenceNumberAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
