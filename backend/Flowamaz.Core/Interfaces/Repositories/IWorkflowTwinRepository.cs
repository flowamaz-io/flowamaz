using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>A workflow due for a scheduled Twin run: its workspace, id and production version.</summary>
public sealed record TwinDueWorkflow(Guid WorkspaceId, Guid WorkflowDefinitionId, Guid ProductionVersionId);

/// <summary>Workflow Twin runs and the run history they are built from. Every query is scoped to one workspace.</summary>
public interface IWorkflowTwinRepository
{
    Task AddAsync(WorkflowTwinRun run, CancellationToken cancellationToken = default);

    /// <summary>Keeps the newest <paramref name="keep"/> runs of a workflow and deletes the rest.</summary>
    Task PruneAsync(Guid workspaceId, Guid workflowDefinitionId, int keep, CancellationToken cancellationToken = default);

    Task<WorkflowTwinRun?> GetLatestAsync(Guid workspaceId, Guid workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>The latest run of every workflow in the workspace that has one.</summary>
    Task<List<WorkflowTwinRun>> GetLatestForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Workflows with a production version whose latest Twin run is for another version or older than
    /// <paramref name="staleBefore"/> (or that have none), oldest first.
    /// </summary>
    Task<List<TwinDueWorkflow>> GetDueAsync(DateTime staleBefore, int limit, CancellationToken cancellationToken = default);

    /// <summary>Trigger payloads of recent real (non-test) runs, newest first.</summary>
    Task<List<string>> GetRecentPayloadsAsync(Guid workspaceId, Guid workflowDefinitionId, int limit, CancellationToken cancellationToken = default);

    /// <summary>The latest recorded output per node id from recent finished runs.</summary>
    Task<Dictionary<string, string>> GetRecordedOutputsAsync(Guid workspaceId, Guid workflowDefinitionId, int runs, CancellationToken cancellationToken = default);
}
