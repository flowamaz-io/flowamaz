using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;

namespace Flowamaz.Tests.Unit.Jobs;

/// <summary>
/// WorkerLeaseExpiryJob (prompt 02-03): an orphaned Running instance (expired lease) is detected and
/// re-queued for pickup by another worker.
/// </summary>
public class WorkerLeaseExpiryJobTests
{
    [Fact]
    public async Task Orphaned_instance_is_requeued()
    {
        var instances = new Mock<IWorkflowInstanceRepository>();
        var queue = new Mock<ITaskQueue>();
        var orphan = new WorkflowInstance
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Status = InstanceStatus.Running,
            WorkerLeaseId = "dead-worker",
            WorkerLeaseExpiresAt = DateTime.UtcNow.AddSeconds(-60),
        };
        instances.Setup(r => r.GetOrphanedAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([orphan]);

        var job = new WorkerLeaseExpiryJob(instances.Object, queue.Object, NullLogger<WorkerLeaseExpiryJob>.Instance);

        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await job.Execute(context.Object);

        queue.Verify(q => q.EnqueueAsync(orphan.WorkspaceId, orphan.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_orphans_does_nothing()
    {
        var instances = new Mock<IWorkflowInstanceRepository>();
        var queue = new Mock<ITaskQueue>();
        instances.Setup(r => r.GetOrphanedAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var job = new WorkerLeaseExpiryJob(instances.Object, queue.Object, NullLogger<WorkerLeaseExpiryJob>.Instance);
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await job.Execute(context.Object);

        queue.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
