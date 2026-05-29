using FluentAssertions;
using Flowamaz.Application.Analytics;
using Flowamaz.Core.Enums;

namespace Flowamaz.Tests.Unit.Analytics;

/// <summary>Deterministic trend + anomaly rules (prompt 05-04).</summary>
public class ProcessTrendAnalyzerTests
{
    private readonly ProcessTrendAnalyzer _analyzer = new();

    [Fact]
    public void FailureRateTrend_increasing_week_over_week_creates_PatternChange()
    {
        var insight = _analyzer.DetectFailureRateTrend("Orders", previousWeekFailureRate: 0.05, currentWeekFailureRate: 0.20);

        insight.Should().NotBeNull();
        insight!.Type.Should().Be(InsightType.PatternChange);
        insight.Severity.Should().Be(InsightSeverity.Warning);
        insight.Message.Should().Contain("failure rate");
    }

    [Fact]
    public void FailureRateTrend_stable_returns_null()
    {
        _analyzer.DetectFailureRateTrend("Orders", 0.10, 0.11).Should().BeNull();
    }

    [Fact]
    public void DurationTrend_over_20pct_increase_creates_Bottleneck()
    {
        var insight = _analyzer.DetectDurationTrend("Orders", previousWeekAvgMs: 1000, currentWeekAvgMs: 1300);

        insight.Should().NotBeNull();
        insight!.Type.Should().Be(InsightType.Bottleneck);
    }

    [Fact]
    public void DurationTrend_within_20pct_returns_null()
    {
        _analyzer.DetectDurationTrend("Orders", 1000, 1150).Should().BeNull();
    }

    [Fact]
    public void NodeFailureRate_over_30pct_creates_node_specific_Bottleneck()
    {
        var insight = _analyzer.DetectNodeFailureRate("Orders", "charge-card", nodeFailureRate: 0.45);

        insight.Should().NotBeNull();
        insight!.Type.Should().Be(InsightType.Bottleneck);
        insight.Message.Should().Contain("charge-card");
    }

    [Fact]
    public void VolumeSpike_over_3_std_dev_creates_AnomalyDetected()
    {
        // daily mean 23, std dev 5 → threshold 23 + 15 = 38; 847 is a clear spike.
        var insight = _analyzer.DetectVolumeSpike("Purchase Approval", todayRuns: 847, dailyMean: 23, dailyStdDev: 5);

        insight.Should().NotBeNull();
        insight!.Type.Should().Be(InsightType.AnomalyDetected);
        insight.Message.Should().Contain("847");
    }

    [Fact]
    public void VolumeSpike_within_normal_range_returns_null()
    {
        _analyzer.DetectVolumeSpike("Orders", todayRuns: 30, dailyMean: 23, dailyStdDev: 5).Should().BeNull();
    }

    [Fact]
    public void StandardDeviation_matches_population_formula()
    {
        ProcessTrendAnalyzer.StandardDeviation(new[] { 2, 4, 4, 4, 5, 5, 7, 9 }).Should().BeApproximately(2.0, 0.0001);
    }
}
