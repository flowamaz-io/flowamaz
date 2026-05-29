using System.Globalization;
using Flowamaz.Core.Enums;

namespace Flowamaz.Application.Analytics;

/// <summary>
/// Deterministic trend + anomaly detection (prompt 05-04). Pure functions — no AI, no state — so the
/// rules are exhaustively unit-testable. ProcessIntelligenceService calls these alongside its SLA
/// checks and persists any returned insights.
/// </summary>
public sealed class ProcessTrendAnalyzer
{
    /// <summary>Week-over-week failure-rate increase of ≥5 percentage points → PatternChange.</summary>
    public TrendInsight? DetectFailureRateTrend(string workflowName, double previousWeekFailureRate, double currentWeekFailureRate)
    {
        if (currentWeekFailureRate - previousWeekFailureRate < 0.05) return null;

        var prevPct = Pct(previousWeekFailureRate);
        var currPct = Pct(currentWeekFailureRate);
        return new TrendInsight(
            InsightType.PatternChange,
            InsightSeverity.Warning,
            $"{workflowName} failure rate rose from {prevPct}% to {currPct}% week-over-week. Review recent changes to the failing nodes.",
            $$"""{"previousFailureRate":{{Num(previousWeekFailureRate)}},"currentFailureRate":{{Num(currentWeekFailureRate)}}}""");
    }

    /// <summary>Average run duration up &gt;20% vs the previous week → Bottleneck.</summary>
    public TrendInsight? DetectDurationTrend(string workflowName, long previousWeekAvgMs, long currentWeekAvgMs)
    {
        if (previousWeekAvgMs <= 0 || currentWeekAvgMs <= previousWeekAvgMs * 1.2) return null;

        var increasePct = (int)Math.Round((currentWeekAvgMs - previousWeekAvgMs) / (double)previousWeekAvgMs * 100);
        return new TrendInsight(
            InsightType.Bottleneck,
            InsightSeverity.Warning,
            $"{workflowName} average run time is up {increasePct}% week-over-week. A node has slowed down — check the slowest step.",
            $$"""{"previousAvgMs":{{previousWeekAvgMs}},"currentAvgMs":{{currentWeekAvgMs}}}""");
    }

    /// <summary>A node failing in &gt;30% of runs → node-specific Bottleneck.</summary>
    public TrendInsight? DetectNodeFailureRate(string workflowName, string nodeId, double nodeFailureRate)
    {
        if (nodeFailureRate <= 0.30) return null;

        return new TrendInsight(
            InsightType.Bottleneck,
            InsightSeverity.Critical,
            $"Node '{nodeId}' in {workflowName} fails in {Pct(nodeFailureRate)}% of runs. It is the dominant failure point — add retries or fix the root cause.",
            $$"""{"nodeId":"{{nodeId}}","failureRate":{{Num(nodeFailureRate)}}}""");
    }

    /// <summary>Today's run count &gt;3 standard deviations above the 30-day mean → AnomalyDetected.</summary>
    public TrendInsight? DetectVolumeSpike(string workflowName, int todayRuns, double dailyMean, double dailyStdDev)
    {
        if (dailyStdDev <= 0) return null;
        if (todayRuns <= dailyMean + 3 * dailyStdDev) return null;

        var avg = (int)Math.Round(dailyMean);
        return new TrendInsight(
            InsightType.AnomalyDetected,
            InsightSeverity.Warning,
            $"{workflowName} processed {todayRuns} runs today vs a {avg}-run daily average — unusual spike. Confirm it's expected (campaign, backfill) and not a runaway trigger.",
            $$"""{"todayRuns":{{todayRuns}},"dailyMean":{{Num(dailyMean)}},"dailyStdDev":{{Num(dailyStdDev)}}}""");
    }

    /// <summary>Population standard deviation of a daily-count series.</summary>
    public static double StandardDeviation(IReadOnlyCollection<int> values)
    {
        if (values.Count == 0) return 0;
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        return Math.Sqrt(variance);
    }

    private static int Pct(double rate) => (int)Math.Round(rate * 100);
    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>A deterministic insight the analyzer wants raised. Maps 1:1 to <c>WorkflowInsight</c>.</summary>
public sealed record TrendInsight(InsightType Type, InsightSeverity Severity, string Message, string DataJson);
