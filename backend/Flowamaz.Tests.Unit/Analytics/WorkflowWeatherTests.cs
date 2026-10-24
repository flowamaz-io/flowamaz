using FluentAssertions;
using Flowamaz.Application.Analytics;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Analytics;

/// <summary>
/// Workflow Weather status bands (prompt 02-07, FUNCTIONAL.md §11.6) — all four conditions,
/// evaluated worst-first.
/// </summary>
public class WorkflowWeatherTests
{
    [Fact]
    public void Red_when_three_failures_or_critical_or_low_sla()
    {
        WorkflowWeatherService.ComputeStatus(failedLastHour: 3, slaCompliancePct: 100, hasCritical: false, hasWarning: false).Should().Be("red");
        WorkflowWeatherService.ComputeStatus(0, 100, hasCritical: true, hasWarning: false).Should().Be("red");
        WorkflowWeatherService.ComputeStatus(0, 50, false, false).Should().Be("red");
    }

    [Fact]
    public void Orange_when_one_failure_or_sla_60_to_80()
    {
        WorkflowWeatherService.ComputeStatus(failedLastHour: 1, slaCompliancePct: 100, hasCritical: false, hasWarning: false).Should().Be("orange");
        WorkflowWeatherService.ComputeStatus(0, 70, false, false).Should().Be("orange");
    }

    [Fact]
    public void Yellow_when_sla_80_to_95_or_warning()
    {
        WorkflowWeatherService.ComputeStatus(0, 90, false, false).Should().Be("yellow");
        WorkflowWeatherService.ComputeStatus(0, 100, hasCritical: false, hasWarning: true).Should().Be("yellow");
    }

    [Fact]
    public void Green_when_healthy()
    {
        WorkflowWeatherService.ComputeStatus(0, 99, false, false).Should().Be("green");
    }

    [Fact]
    public async Task RunsThisMonth_is_the_live_production_run_count_not_the_hourly_metric_rollup()
    {
        var ws = Guid.NewGuid();
        var definitions = new Mock<IWorkflowDefinitionRepository>();
        definitions.Setup(d => d.GetForWorkspaceAsync(ws, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var metrics = new Mock<IWorkflowMetricRepository>();
        metrics.Setup(m => m.GetLatestPerWorkflowAsync(ws, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        metrics.Setup(m => m.GetRunCountSinceAsync(ws, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var insights = new Mock<IWorkflowInsightRepository>();
        insights.Setup(i => i.GetUnacknowledgedForWorkspaceAsync(ws, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var analytics = new Mock<IWorkflowAnalyticsRepository>();
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        analytics.Setup(a => a.GetProductionRunCountSinceAsync(ws, monthStart, It.IsAny<CancellationToken>())).ReturnsAsync(5);

        var service = new WorkflowWeatherService(definitions.Object, metrics.Object, insights.Object, analytics.Object,
            NullLogger<WorkflowWeatherService>.Instance);
        var weather = await service.GetWeatherAsync(ws);

        weather.RunsThisMonth.Should().Be(5);
    }
}
