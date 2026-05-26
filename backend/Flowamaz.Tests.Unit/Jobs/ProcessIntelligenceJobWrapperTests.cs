using FluentAssertions;
using Flowamaz.Application.Analytics;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;

namespace Flowamaz.Tests.Unit.Jobs;

/// <summary>
/// ProcessIntelligenceJob (fix-02-01): the thin Quartz wrapper delegates to
/// <see cref="ProcessIntelligenceService"/> and swallows errors so a failed analytics pass never
/// stops the scheduler. The rich analytics logic is covered by ProcessIntelligenceJobTests.
/// </summary>
public class ProcessIntelligenceJobWrapperTests
{
    private readonly Mock<IWorkflowAnalyticsRepository> _analytics = new();
    private readonly Mock<IWorkflowMetricRepository> _metrics = new();
    private readonly Mock<IWorkflowInsightRepository> _insights = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IModelResolutionService> _modelResolution = new();
    private readonly Mock<IAiCompletionService> _completion = new();
    private readonly Mock<IAiTokenMeteringService> _metering = new();
    private readonly Mock<ISemanticCacheService> _semanticCache = new();

    private ProcessIntelligenceService NewService() => new(
        _analytics.Object, _metrics.Object, _insights.Object, _unitOfWork.Object,
        _modelResolution.Object, _completion.Object, _metering.Object, _semanticCache.Object,
        NullLogger<ProcessIntelligenceService>.Instance);

    private ProcessIntelligenceJob NewJob() =>
        new(NewService(), NullLogger<ProcessIntelligenceJob>.Instance);

    private static IJobExecutionContext Context()
    {
        var ctx = new Mock<IJobExecutionContext>();
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Fact]
    public async Task Execute_RunsTheService()
    {
        _analytics.Setup(a => a.GetWorkspacesWithRunsSinceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await NewJob().Execute(Context());

        _analytics.Verify(a => a.GetWorkspacesWithRunsSinceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_ServiceThrows_LogsAndDoesNotPropagate()
    {
        _analytics.Setup(a => a.GetWorkspacesWithRunsSinceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("analytics db down"));

        var act = async () => await NewJob().Execute(Context());

        await act.Should().NotThrowAsync();
    }
}
