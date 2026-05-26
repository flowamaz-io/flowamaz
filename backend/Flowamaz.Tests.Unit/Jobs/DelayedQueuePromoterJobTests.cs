using FluentAssertions;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;

namespace Flowamaz.Tests.Unit.Jobs;

/// <summary>
/// DelayedQueuePromoterJob (fix-02-01): promotes delayed-queue items whose execute-after time has
/// passed back onto the workspace pending queue, and never lets an enqueue failure escape the job.
/// </summary>
public class DelayedQueuePromoterJobTests
{
    private readonly Mock<ITaskQueue> _queue = new();

    private DelayedQueuePromoterJob NewJob() =>
        new(_queue.Object, NullLogger<DelayedQueuePromoterJob>.Instance);

    private static IJobExecutionContext Context()
    {
        var ctx = new Mock<IJobExecutionContext>();
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Fact]
    public async Task Execute_NoReadyItems_DoesNothing()
    {
        _queue.Setup(q => q.GetDelayedReadyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await NewJob().Execute(Context());

        _queue.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_ReadyItems_PromotesEachToPendingQueue()
    {
        var items = new List<DelayedQueueItem>
        {
            new(Guid.NewGuid(), Guid.NewGuid()),
            new(Guid.NewGuid(), Guid.NewGuid()),
            new(Guid.NewGuid(), Guid.NewGuid()),
        };
        _queue.Setup(q => q.GetDelayedReadyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        await NewJob().Execute(Context());

        foreach (var item in items)
        {
            _queue.Verify(q => q.EnqueueAsync(item.WorkspaceId, item.InstanceId, It.IsAny<CancellationToken>()), Times.Once);
        }
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Execute_EnqueueThrows_LogsAndDoesNotPropagate()
    {
        _queue.Setup(q => q.GetDelayedReadyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DelayedQueueItem(Guid.NewGuid(), Guid.NewGuid())]);
        _queue.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var act = async () => await NewJob().Execute(Context());

        await act.Should().NotThrowAsync();
    }
}
