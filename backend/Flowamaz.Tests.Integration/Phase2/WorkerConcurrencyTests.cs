using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Flowamaz.Tests.Integration.Phase2;

/// <summary>
/// Worker queue concurrency over real Postgres (prompt 02-08): concurrent FOR UPDATE SKIP LOCKED
/// claims never grab the same instance, and an expired-lease Running instance is recoverable.
/// </summary>
public class WorkerConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("flowamaz_concurrency_test")
        .WithUsername("flowamaz")
        .WithPassword("flowamaz_test_pw")
        .Build();

    private readonly Guid _workspaceId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private FlowAmazDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    [Fact]
    public async Task Ten_concurrent_claims_never_double_claim()
    {
        await using (var seed = NewContext())
        {
            for (var i = 0; i < 10; i++)
            {
                seed.WorkflowInstances.Add(new WorkflowInstance
                {
                    WorkspaceId = _workspaceId,
                    WorkflowDefinitionId = Guid.NewGuid(),
                    WorkflowVersionId = Guid.NewGuid(),
                    Status = InstanceStatus.Pending,
                });
            }
            await seed.SaveChangesAsync();
        }

        // Two workers on independent connections claim concurrently.
        await using var dbA = NewContext();
        await using var dbB = NewContext();
        var repoA = new WorkflowInstanceRepository(dbA);
        var repoB = new WorkflowInstanceRepository(dbB);

        var claims = await Task.WhenAll(
            repoA.GetPendingForWorkerAsync("worker-A", 10),
            repoB.GetPendingForWorkerAsync("worker-B", 10));

        var idsA = claims[0].Select(i => i.Id).ToHashSet();
        var idsB = claims[1].Select(i => i.Id).ToHashSet();

        idsA.Overlaps(idsB).Should().BeFalse("SKIP LOCKED must never hand the same instance to two workers");
        var union = idsA.Concat(idsB).ToList();
        union.Should().OnlyHaveUniqueItems();
        union.Count.Should().BeLessThanOrEqualTo(10);
        union.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Expired_lease_running_instance_is_recoverable()
    {
        Guid instanceId;
        await using (var seed = NewContext())
        {
            var instance = new WorkflowInstance
            {
                WorkspaceId = _workspaceId,
                WorkflowDefinitionId = Guid.NewGuid(),
                WorkflowVersionId = Guid.NewGuid(),
                Status = InstanceStatus.Running,
                WorkerLeaseId = "dead-worker",
                WorkerLeaseExpiresAt = DateTime.UtcNow.AddMinutes(-2),
            };
            seed.WorkflowInstances.Add(instance);
            await seed.SaveChangesAsync();
            instanceId = instance.Id;
        }

        await using var db = NewContext();
        var repo = new WorkflowInstanceRepository(db);
        var orphaned = await repo.GetOrphanedAsync(DateTime.UtcNow);

        orphaned.Select(i => i.Id).Should().Contain(instanceId);
    }
}
