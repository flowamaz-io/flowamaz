using FluentAssertions;
using Flowamaz.Application.Analytics;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Analytics;

/// <summary>
/// RoiAnalyticsService deterministic calculations (prompt 05-04): time saved = manual minutes ×
/// successful runs, cost avoided = (manual − automation) × successful, ROI % = avoided ÷ automation × 100.
/// </summary>
public class RoiAnalyticsServiceTests
{
    private readonly Mock<IWorkflowRoiConfigRepository> _configs = new();
    private readonly Mock<IWorkflowAnalyticsRepository> _analytics = new();
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _wf = Guid.NewGuid();

    private RoiAnalyticsService NewService() =>
        new(_configs.Object, _analytics.Object, _definitions.Object, _uow.Object, NullLogger<RoiAnalyticsService>.Instance);

    private void Arrange(int runsTotal, int runsCompleted, int manualMinutes, decimal manualCost, decimal autoCost)
    {
        _analytics.Setup(m => m.GetProductionRunCountsAsync(_ws, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowRunCounts(_wf, runsTotal, runsCompleted)]);
        _configs.Setup(c => c.GetForWorkspaceAsync(_ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowRoiConfig
            {
                WorkspaceId = _ws, WorkflowDefinitionId = _wf,
                ManualProcessTimeMinutes = manualMinutes,
                ManualProcessCostPerRunUsd = manualCost,
                AutomationCostPerRunUsd = autoCost,
                MonthlyCurrency = "MYR",
            }]);
        _definitions.Setup(d => d.GetForWorkspaceAsync(_ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowDefinition { Id = _wf, WorkspaceId = _ws, Name = "Purchase Approval", Slug = "pa" }]);
    }

    [Fact]
    public async Task GetWorkspaceRoi_computes_time_saved_from_successful_runs()
    {
        Arrange(runsTotal: 10, runsCompleted: 8, manualMinutes: 60, manualCost: 50m, autoCost: 5m);

        var summary = await NewService().GetWorkspaceRoiAsync(_ws, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        summary.SuccessfulRuns.Should().Be(8);
        summary.TotalTimeSavedMinutes.Should().Be(480); // 60 * 8
        summary.ByWorkflow.Should().ContainSingle().Which.TimeSavedMinutes.Should().Be(480);
    }

    [Fact]
    public async Task GetWorkspaceRoi_roi_percentage_formula_is_cost_avoided_over_automation_cost()
    {
        // manual 50, automation 5 → avoided 45/run; 8 successful → avoided 360; automation total 40.
        Arrange(runsTotal: 10, runsCompleted: 8, manualMinutes: 60, manualCost: 50m, autoCost: 5m);

        var summary = await NewService().GetWorkspaceRoiAsync(_ws, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        summary.TotalCostAvoided.Should().Be(360m);
        summary.RoiPercentage.Should().Be(900m);  // 360 / 40 * 100
        summary.AvgCostPerRun.Should().Be(5m);     // 40 / 8
    }

    [Fact]
    public async Task GetWorkspaceRoi_unconfigured_workflow_reports_zero_value_and_not_configured()
    {
        _analytics.Setup(m => m.GetProductionRunCountsAsync(_ws, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowRunCounts(_wf, 5, 5)]);
        _configs.Setup(c => c.GetForWorkspaceAsync(_ws, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _definitions.Setup(d => d.GetForWorkspaceAsync(_ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowDefinition { Id = _wf, WorkspaceId = _ws, Name = "Unconfigured", Slug = "u" }]);

        var summary = await NewService().GetWorkspaceRoiAsync(_ws, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        var detail = summary.ByWorkflow.Should().ContainSingle().Subject;
        detail.Configured.Should().BeFalse();
        detail.CostAvoided.Should().Be(0m);
        summary.TotalCostAvoided.Should().Be(0m);
    }

    [Fact]
    public async Task GetWorkspaceRoi_counts_live_runs_without_waiting_for_hourly_metrics()
    {
        // B11: runs created minutes ago (no workflow_metrics row yet) must already count.
        Arrange(runsTotal: 5, runsCompleted: 3, manualMinutes: 10, manualCost: 0m, autoCost: 0m);

        var summary = await NewService().GetWorkspaceRoiAsync(_ws, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        summary.TotalRunsInPeriod.Should().Be(5);
        summary.SuccessfulRuns.Should().Be(3);
        _analytics.Verify(a => a.GetProductionRunCountsAsync(_ws, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertConfig_returns_null_when_workflow_not_in_workspace()
    {
        _definitions.Setup(d => d.GetByIdForWorkspaceAsync(_wf, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        var result = await NewService().UpsertConfigAsync(_ws, _wf, new RoiConfigDto(60, 50m, 5m, "MYR"));

        result.Should().BeNull();
    }
}
