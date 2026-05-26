using FluentAssertions;
using Flowamaz.Application.Analytics;

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
}
