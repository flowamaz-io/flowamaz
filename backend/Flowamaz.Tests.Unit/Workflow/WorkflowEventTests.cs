using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Covers the append-only event log repository: sequence numbers increase monotonically and reads
/// come back in sequence order. The repository exposes no update/delete — the log is immutable.
/// </summary>
public class WorkflowEventTests
{
    private static FlowAmazDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"events-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task GetNextSequenceNumber_starts_at_one_then_increments()
    {
        await using var db = NewDb();
        var repo = new WorkflowEventRepository(db);
        var instanceId = Guid.NewGuid();

        (await repo.GetNextSequenceNumberAsync(instanceId)).Should().Be(1);

        await AppendAsync(db, repo, instanceId, 1, "InstanceStarted");
        (await repo.GetNextSequenceNumberAsync(instanceId)).Should().Be(2);

        await AppendAsync(db, repo, instanceId, 2, "NodeStarted");
        (await repo.GetNextSequenceNumberAsync(instanceId)).Should().Be(3);
    }

    [Fact]
    public async Task GetForInstance_returns_events_in_sequence_order()
    {
        await using var db = NewDb();
        var repo = new WorkflowEventRepository(db);
        var instanceId = Guid.NewGuid();

        await AppendAsync(db, repo, instanceId, 2, "NodeCompleted");
        await AppendAsync(db, repo, instanceId, 1, "InstanceStarted");
        await AppendAsync(db, repo, instanceId, 3, "InstanceCompleted");

        var events = await repo.GetForInstanceAsync(instanceId);

        events.Select(e => e.SequenceNumber).Should().ContainInOrder(1L, 2L, 3L);
        events.Select(e => e.EventType).Should()
            .ContainInOrder("InstanceStarted", "NodeCompleted", "InstanceCompleted");
    }

    [Fact]
    public async Task Sequence_numbers_are_isolated_per_instance()
    {
        await using var db = NewDb();
        var repo = new WorkflowEventRepository(db);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await AppendAsync(db, repo, a, 1, "InstanceStarted");
        await AppendAsync(db, repo, a, 2, "InstanceCompleted");

        (await repo.GetNextSequenceNumberAsync(b)).Should().Be(1);
        (await repo.GetNextSequenceNumberAsync(a)).Should().Be(3);
    }

    private static async Task AppendAsync(
        FlowAmazDbContext db, WorkflowEventRepository repo, Guid instanceId, long seq, string type)
    {
        await repo.AppendAsync(new WorkflowEvent
        {
            WorkspaceId = Guid.NewGuid(),
            InstanceId = instanceId,
            SequenceNumber = seq,
            EventType = type,
            OccurredAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
