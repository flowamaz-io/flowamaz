using FluentAssertions;
using Flowamaz.Infrastructure.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Flowamaz.Tests.Integration.Queue;

/// <summary>
/// Real-Redis proof of the task queue (prompt 02-02): the enqueue → dequeue → acknowledge
/// round-trip works, the claim is atomic (a second dequeue sees nothing — no double-claim), the
/// delayed sorted-set only releases due items, and active workspaces are tracked.
/// </summary>
public class RedisTaskQueueTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
    private IConnectionMultiplexer _mux = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _mux.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private RedisTaskQueue NewQueue() => new(_mux, NullLogger<RedisTaskQueue>.Instance);

    [Fact]
    public async Task Enqueue_dequeue_acknowledge_round_trip()
    {
        var queue = NewQueue();
        var ws = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await queue.EnqueueAsync(ws, instanceId);

        (await queue.GetActiveWorkspacesAsync()).Should().Contain(ws);

        var dequeued = await queue.DequeueAsync(ws, "worker-1");
        dequeued.Should().Be(instanceId);

        await queue.AcknowledgeAsync(ws, instanceId);

        (await queue.DequeueAsync(ws, "worker-1")).Should().BeNull("the item was acknowledged");
    }

    [Fact]
    public async Task Dequeue_is_atomic_no_double_claim()
    {
        var queue = NewQueue();
        var ws = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        await queue.EnqueueAsync(ws, instanceId);

        var first = await queue.DequeueAsync(ws, "worker-A");
        var second = await queue.DequeueAsync(ws, "worker-B");

        first.Should().Be(instanceId);
        second.Should().BeNull("RPOPLPUSH moved the only item to processing — a second worker gets nothing");
    }

    [Fact]
    public async Task ReturnToQueue_makes_item_dequeuable_again()
    {
        var queue = NewQueue();
        var ws = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        await queue.EnqueueAsync(ws, instanceId);
        await queue.DequeueAsync(ws, "worker-A");

        await queue.ReturnToQueueAsync(ws, instanceId);

        (await queue.DequeueAsync(ws, "worker-B")).Should().Be(instanceId);
    }

    [Fact]
    public async Task Delayed_items_release_only_when_due()
    {
        var queue = NewQueue();
        var ws = Guid.NewGuid();
        var dueNow = Guid.NewGuid();
        var dueLater = Guid.NewGuid();

        await queue.EnqueueDelayedAsync(ws, dueNow, delaySeconds: 0);
        await queue.EnqueueDelayedAsync(ws, dueLater, delaySeconds: 3600);

        var ready = await queue.GetDelayedReadyAsync();

        ready.Should().ContainSingle();
        ready[0].WorkspaceId.Should().Be(ws);
        ready[0].InstanceId.Should().Be(dueNow);

        // The due item is consumed; a second poll finds nothing new.
        (await queue.GetDelayedReadyAsync()).Should().BeEmpty();
    }
}
