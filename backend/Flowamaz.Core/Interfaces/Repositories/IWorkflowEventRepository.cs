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
    Task<List<WorkflowEvent>> GetForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Next sequence number for the instance — (max persisted) + 1, or 1 for the first event.</summary>
    Task<long> GetNextSequenceNumberAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
