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
public sealed class WorkflowInstanceRepository(EngineDbContext db) : IWorkflowInstanceRepository
{
    // 30-second worker lease (prompt 02-01). Renewed every 10s by the worker.

    public Task<WorkflowInstance?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id && i.WorkspaceId == workspaceId, cancellationToken);

    public async Task LockForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM workflow_instances WHERE id = {id} FOR UPDATE", cancellationToken);
        var tracked = db.ChangeTracker.Entries<WorkflowInstance>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked is not null) await tracked.ReloadAsync(cancellationToken);
    }

    public async Task<InstanceStatus?> GetCurrentStatusAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.WorkflowInstances.AsNoTracking().IgnoreQueryFilters().Where(i => i.Id == id)
            .Select(i => (InstanceStatus?)i.Status).FirstOrDefaultAsync(cancellationToken);

    public Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<WorkflowInstance?> GetByIdempotencyKeyAsync(Guid workspaceId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.FirstOrDefaultAsync(
            i => i.WorkspaceId == workspaceId && i.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<List<WorkflowInstance>> GetForWorkspaceAsync(Guid workspaceId, bool includeTest = false, CancellationToken cancellationToken = default) =>
        db.WorkflowInstances.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && (includeTest || !i.IsTest))
            .OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);

    public async Task<int> SoftDeleteExpiredTestInstancesAsync(DateTime asOf, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
        {
            return await NonRelationalAsync(async () =>
            {
                var expired = await db.WorkflowInstances
                    .Where(i => i.IsTest && i.TestExpiresAt < asOf).ToListAsync(cancellationToken);
                foreach (var run in expired) run.IsDeleted = true;
                await db.SaveChangesAsync(cancellationToken);
                return expired.Count;
            }, cancellationToken);
        }
        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE workflow_instances
            SET is_deleted = true, updated_at = NOW()
            WHERE is_test = true AND test_expires_at < {asOf} AND is_deleted = false
            """, cancellationToken);
    }

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
        if (!db.Database.IsRelational())
        {
            return await NonRelationalAsync(async () =>
            {
                var now = DateTime.UtcNow;
                var claimed = await db.WorkflowInstances
                    .Where(i => i.Status == InstanceStatus.Pending && (i.WorkerLeaseExpiresAt == null || i.WorkerLeaseExpiresAt < now))
                    .OrderBy(i => i.CreatedAt).Take(batchSize).ToListAsync(cancellationToken);
                foreach (var run in claimed) SetLease(run, leaseId, now);
                await db.SaveChangesAsync(cancellationToken);
                return claimed;
            }, cancellationToken);
        }

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
        if (!db.Database.IsRelational())
        {
            return await UpdateLeaseAsync(instanceId, (run, now) =>
                run.WorkerLeaseId is null || run.WorkerLeaseId == leaseId || run.WorkerLeaseExpiresAt is null || run.WorkerLeaseExpiresAt < now,
                (run, now) => SetLease(run, leaseId, now), cancellationToken);
        }

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
        if (!db.Database.IsRelational())
        {
            return await UpdateLeaseAsync(instanceId, (run, _) => run.WorkerLeaseId == leaseId,
                (run, now) => run.WorkerLeaseExpiresAt = now.AddSeconds(LeaseSeconds), cancellationToken);
        }

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
        if (!db.Database.IsRelational())
        {
            return await UpdateLeaseAsync(instanceId, (run, _) => run.WorkerLeaseId == leaseId,
                (run, _) => { run.WorkerLeaseId = null; run.WorkerLeaseExpiresAt = null; }, cancellationToken);
        }

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

    // ── Without a relational database (EF Core's in-memory provider, in the in-memory host and tests) ──
    // The claim and leases above are single SQL statements so that several workers on several machines
    // can share one database. Without SQL they are done here, one at a time per process; that is enough
    // for one host process, which is all a non-relational store supports. Unlike the SQL versions they
    // save through the context, so call them (as the worker does) on a scope with nothing else pending.

    private const int LeaseSeconds = 30;
    private static readonly SemaphoreSlim NonRelationalGate = new(1, 1);

    private static void SetLease(WorkflowInstance run, string leaseId, DateTime now)
    {
        run.WorkerLeaseId = leaseId;
        run.WorkerLeaseExpiresAt = now.AddSeconds(LeaseSeconds);
    }

    private static async Task<T> NonRelationalAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken)
    {
        await NonRelationalGate.WaitAsync(cancellationToken);
        try { return await body(); }
        finally { NonRelationalGate.Release(); }
    }

    private Task<bool> UpdateLeaseAsync(
        Guid instanceId, Func<WorkflowInstance, DateTime, bool> when, Action<WorkflowInstance, DateTime> change,
        CancellationToken cancellationToken) =>
        NonRelationalAsync(async () =>
        {
            var run = await db.WorkflowInstances.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);
            var now = DateTime.UtcNow;
            if (run is null || !when(run, now)) return false;
            change(run, now);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
}
