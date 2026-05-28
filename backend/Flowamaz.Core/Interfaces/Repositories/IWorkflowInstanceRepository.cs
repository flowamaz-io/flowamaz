using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for workflow instances, including the worker queue claim. The claim
/// (<see cref="GetPendingForWorkerAsync"/>) uses a raw <c>FOR UPDATE SKIP LOCKED</c> query so
/// competing workers never grab the same instance; it commits on its own. Other writes stage
/// and commit via <see cref="Persistence.IUnitOfWork"/>.
/// </summary>
public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an instance by id with no workspace filter — for the trusted worker/orchestrator,
    /// which has already authorised the work via the queue claim + DB lease. Callers use the
    /// returned instance's <c>WorkspaceId</c> to scope every follow-on query.
    /// </summary>
    Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetByIdempotencyKeyAsync(Guid workspaceId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<List<WorkflowInstance>> GetForWorkspaceAsync(Guid workspaceId, bool includeTest = false, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes test instances whose <see cref="WorkflowInstance.TestExpiresAt"/> is before <paramref name="asOf"/>.</summary>
    Task<int> SoftDeleteExpiredTestInstancesAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>True if the definition has any non-terminal instance (Pending/Running/Waiting/Compensating).</summary>
    Task<bool> HasActiveInstancesAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
    void Update(WorkflowInstance instance);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> Pending instances whose lease is free
    /// or expired, stamping them with <paramref name="leaseId"/> and a fresh expiry. Uses
    /// <c>FOR UPDATE SKIP LOCKED</c>; returns the claimed rows. Persists immediately.
    /// </summary>
    Task<List<WorkflowInstance>> GetPendingForWorkerAsync(string leaseId, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps the lease for one specific instance if it is free, expired, or already this worker's.
    /// Used by the worker after a Redis dequeue to bind that instance to its DB lease. Returns false
    /// if another worker holds a live lease.
    /// </summary>
    Task<bool> TryAcquireLeaseAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>Extends the lease expiry if <paramref name="leaseId"/> still owns the instance. Returns false if lost.</summary>
    Task<bool> RenewLeaseAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>Clears the lease if <paramref name="leaseId"/> owns the instance. Returns false if not owner.</summary>
    Task<bool> ReleaseLeaseAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Running instances whose worker lease has expired as of <paramref name="asOf"/> — orphaned by a
    /// worker that died without releasing. The lease-expiry timer re-queues these.
    /// </summary>
    Task<List<WorkflowInstance>> GetOrphanedAsync(DateTime asOf, CancellationToken cancellationToken = default);
}
