using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Workspace-scoped persistence for instances plus the worker queue claim. The claim must run as a
/// top-level statement (a data-modifying CTE cannot be nested in a subquery), so the call uses
/// <c>IgnoreQueryFilters</c> with no further composition to stop EF wrapping it.
/// </summary>
public sealed class WorkflowInstanceRepository(FlowAmazDbContext db) : IWorkflowInstanceRepository
{
    // 30-second worker lease (prompt 02-01). Renewed every 10s by the worker.

    public Task<WorkflowInstance?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id && i.WorkspaceId == workspaceId, cancellationToken);

    public Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<WorkflowInstance?> GetByIdempotencyKeyAsync(Guid workspaceId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.FirstOrDefaultAsync(
            i => i.WorkspaceId == workspaceId && i.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<List<WorkflowInstance>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AsNoTracking().Where(i => i.WorkspaceId == workspaceId)
            .OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);

    private static readonly InstanceStatus[] ActiveStatuses =
        [InstanceStatus.Pending, InstanceStatus.Running, InstanceStatus.Waiting, InstanceStatus.Compensating];

    public Task<bool> HasActiveInstancesAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AnyAsync(
            i => i.WorkspaceId == workspaceId
                 && i.WorkflowDefinitionId == workflowDefinitionId
                 && ActiveStatuses.Contains(i.Status),
            cancellationToken);

    public async Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default) =>
        await db.WorkflowInstances.AddAsync(instance, cancellationToken);

    public void Update(WorkflowInstance instance) => db.WorkflowInstances.Update(instance);

    public async Task<List<WorkflowInstance>> GetPendingForWorkerAsync(string leaseId, int batchSize, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH claimed AS (
                UPDATE workflow_instances
                SET worker_lease_id = {0},
                    worker_lease_expires_at = NOW() + INTERVAL '30 seconds',
                    updated_at = NOW()
                WHERE id IN (
                    SELECT id FROM workflow_instances
                    WHERE status = 'Pending'
                      AND is_deleted = false
                      AND (worker_lease_expires_at IS NULL OR worker_lease_expires_at < NOW())
                    ORDER BY created_at
                    LIMIT {1}
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *
            )
            SELECT * FROM claimed
            """;

        // AsNoTracking so the returned rows carry the RETURNING * lease values rather than any
        // stale already-tracked copies (EF identity resolution would otherwise discard the DB
        // values). IgnoreQueryFilters + no further composition keeps the data-modifying CTE
        // top-level — EF must not wrap it in a subquery (Postgres forbids that).
        return await db.WorkflowInstances
            .FromSqlRaw(sql, leaseId, batchSize)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryAcquireLeaseAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE workflow_instances
            SET worker_lease_id = {leaseId},
                worker_lease_expires_at = NOW() + INTERVAL '30 seconds',
                updated_at = NOW()
            WHERE id = {instanceId}
              AND (worker_lease_id IS NULL OR worker_lease_id = {leaseId} OR worker_lease_expires_at IS NULL OR worker_lease_expires_at < NOW())
            """,
            cancellationToken);
        return rows > 0;
    }

    public async Task<bool> RenewLeaseAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default)
    {
        // INTERVAL is a fixed literal — only id/leaseId are parameterised.
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE workflow_instances
            SET worker_lease_expires_at = NOW() + INTERVAL '30 seconds',
                updated_at = NOW()
            WHERE id = {instanceId} AND worker_lease_id = {leaseId}
            """,
            cancellationToken);
        return rows > 0;
    }

    public async Task<bool> ReleaseLeaseAsync(Guid instanceId, string leaseId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE workflow_instances
            SET worker_lease_id = NULL, worker_lease_expires_at = NULL, updated_at = NOW()
            WHERE id = {instanceId} AND worker_lease_id = {leaseId}
            """,
            cancellationToken);
        return rows > 0;
    }

    public Task<List<WorkflowInstance>> GetOrphanedAsync(DateTime asOf, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Status == InstanceStatus.Running
                        && i.WorkerLeaseExpiresAt != null
                        && i.WorkerLeaseExpiresAt < asOf)
            .ToListAsync(cancellationToken);
}
