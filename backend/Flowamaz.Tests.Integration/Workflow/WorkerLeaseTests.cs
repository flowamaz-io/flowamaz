using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Flowamaz.Tests.Integration.Workflow;

/// <summary>
/// Real-Postgres proof of the worker queue (prompt 02-01): the FOR UPDATE SKIP LOCKED claim stamps
/// a lease and never re-grabs already-leased instances; lease renew/release respect ownership; and
/// the idempotency_key unique index blocks duplicate triggers at the database.
/// </summary>
public class WorkerLeaseTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("flowamaz_lease_test")
        .WithUsername("flowamaz")
        .WithPassword("flowamaz_test_pw")
        .Build();

    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _definitionId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private FlowAmazDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private WorkflowInstance NewPending(string? idempotencyKey = null) => new()
    {
        WorkspaceId = _workspaceId,
        WorkflowDefinitionId = _definitionId,
        WorkflowVersionId = _versionId,
        Status = InstanceStatus.Pending,
        TriggerType = InstanceTriggerType.Manual,
        IdempotencyKey = idempotencyKey,
    };

    [Fact]
    public async Task GetPendingForWorker_claims_a_batch_and_stamps_the_lease()
    {
        await using var db = NewContext();
        db.WorkflowInstances.AddRange(NewPending(), NewPending(), NewPending());
        await db.SaveChangesAsync();

        var repo = new WorkflowInstanceRepository(db);
        var claimed = await repo.GetPendingForWorkerAsync("worker-A", batchSize: 2);

        claimed.Should().HaveCount(2);
        claimed.Should().OnlyContain(i => i.WorkerLeaseId == "worker-A");
        claimed.Should().OnlyContain(i => i.WorkerLeaseExpiresAt != null && i.WorkerLeaseExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task GetPendingForWorker_does_not_reclaim_already_leased_instances()
    {
        await using var db = NewContext();
        db.WorkflowInstances.AddRange(NewPending(), NewPending(), NewPending());
        await db.SaveChangesAsync();

        var repo = new WorkflowInstanceRepository(db);
        var first = await repo.GetPendingForWorkerAsync("worker-A", batchSize: 2);
        var second = await repo.GetPendingForWorkerAsync("worker-B", batchSize: 5);

        first.Should().HaveCount(2);
        second.Should().HaveCount(1, "two of three are still under a live lease held by worker-A");
        second[0].WorkerLeaseId.Should().Be("worker-B");

        // A third pass finds nothing free.
        (await repo.GetPendingForWorkerAsync("worker-C", batchSize: 5)).Should().BeEmpty();
    }

    [Fact]
    public async Task RenewLease_and_ReleaseLease_respect_ownership()
    {
        await using var db = NewContext();
        var instance = NewPending();
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        var repo = new WorkflowInstanceRepository(db);
        await repo.GetPendingForWorkerAsync("worker-A", batchSize: 1);

        (await repo.RenewLeaseAsync(instance.Id, "worker-B")).Should().BeFalse("worker-B does not own the lease");
        (await repo.RenewLeaseAsync(instance.Id, "worker-A")).Should().BeTrue();

        (await repo.ReleaseLeaseAsync(instance.Id, "worker-B")).Should().BeFalse();
        (await repo.ReleaseLeaseAsync(instance.Id, "worker-A")).Should().BeTrue();

        // Released → claimable again.
        (await repo.GetPendingForWorkerAsync("worker-C", batchSize: 1)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Duplicate_idempotency_key_violates_unique_index()
    {
        await using var db = NewContext();
        db.WorkflowInstances.Add(NewPending("dup-key-123"));
        await db.SaveChangesAsync();

        db.WorkflowInstances.Add(NewPending("dup-key-123"));
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
