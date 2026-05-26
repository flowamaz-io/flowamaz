using FluentAssertions;
using Flowamaz.Application.Analytics;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Analytics;

/// <summary>
/// SLA-risk wiring (fix-02-02): the per-workflow SlaThresholdMs now flows from the definition into
/// the hourly metric, so a workflow averaging more than 80% of its SLA raises an SlaRisk insight, and
/// a workflow with no SLA never does.
/// </summary>
public class ProcessIntelligenceServiceSlaTests
{
    private readonly Mock<IWorkflowAnalyticsRepository> _analytics = new();
    private readonly Mock<IWorkflowMetricRepository> _metrics = new();
    private readonly Mock<IWorkflowInsightRepository> _insights = new();
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IModelResolutionService> _modelResolution = new();
    private readonly Mock<IAiCompletionService> _completion = new();
    private readonly Mock<IAiTokenMeteringService> _metering = new();
    private readonly Mock<ISemanticCacheService> _semanticCache = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _def = Guid.NewGuid();

    private ProcessIntelligenceService NewService() => new(
        _analytics.Object, _metrics.Object, _insights.Object, _definitions.Object, _unitOfWork.Object,
        _modelResolution.Object, _completion.Object, _metering.Object, _semanticCache.Object,
        NullLogger<ProcessIntelligenceService>.Instance);

    private void SetupRun(long? slaThresholdMs, long completedDurationMs)
    {
        _analytics.Setup(a => a.GetWorkspacesWithRunsSinceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_ws]);
        _analytics.Setup(a => a.GetWorkflowIdsWithRunsSinceAsync(_ws, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_def]);
        _analytics.Setup(a => a.GetDurationsAsync(_ws, _def, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InstanceDurationSample(InstanceStatus.Completed, completedDurationMs)]);
        _analytics.Setup(a => a.GetBottleneckAsync(_ws, _def, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BottleneckSample?)null);
        _definitions.Setup(d => d.GetByIdForWorkspaceAsync(_def, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition { Id = _def, WorkspaceId = _ws, Name = "Orders", SlaThresholdMs = slaThresholdMs });
        // No history → GenerateAiInsightsAsync returns before any AI call.
        _metrics.Setup(m => m.GetForDefinitionSinceAsync(_def, _ws, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task RunAsync_SlaThresholdSet_AvgOver80pct_RaisesSlaRiskInsight()
    {
        SetupRun(slaThresholdMs: 100, completedDurationMs: 85); // 85 > 80% of 100
        WorkflowInsight? raised = null;
        _insights.Setup(i => i.ReplaceUnacknowledgedAsync(It.IsAny<WorkflowInsight>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInsight, CancellationToken>((ins, _) => raised = ins)
            .Returns(Task.CompletedTask);

        await NewService().RunAsync();

        raised.Should().NotBeNull();
        raised!.InsightType.Should().Be(InsightType.SlaRisk);
        raised.WorkflowDefinitionId.Should().Be(_def);
        _metrics.Verify(m => m.UpsertAsync(It.Is<WorkflowMetric>(x => x.SlaThresholdMs == 100), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_NoSlaThreshold_RaisesNoSlaRiskInsight()
    {
        SetupRun(slaThresholdMs: null, completedDurationMs: 999999);

        await NewService().RunAsync();

        _insights.Verify(i => i.ReplaceUnacknowledgedAsync(
            It.Is<WorkflowInsight>(x => x.InsightType == InsightType.SlaRisk), It.IsAny<CancellationToken>()), Times.Never);
    }
}
