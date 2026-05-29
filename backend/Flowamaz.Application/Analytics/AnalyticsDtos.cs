namespace Flowamaz.Application.Analytics;

public sealed record WeatherResponse(
    DateTime GeneratedAt,
    int TotalWorkflows,
    int ActiveInstances,
    int RunsThisMonth,
    IReadOnlyList<WorkflowWeather> Workflows);

public sealed record WorkflowWeather(
    Guid WorkflowId,
    string WorkflowName,
    string Status,
    int ActiveInstances,
    int FailedLastHour,
    int SlaCompliancePct,
    int PendingInsights,
    string? LatestInsight);

public sealed record InsightResponse(
    Guid Id,
    Guid WorkflowDefinitionId,
    Guid? InstanceId,
    string InsightType,
    string Severity,
    string Message,
    string Data,
    bool IsAcknowledged,
    DateTime? AcknowledgedAt,
    DateTime? ExpiresAt,
    DateTime CreatedAt);

// ── ROI analytics (prompt 05-04) ──────────────────────────────────────────────

public sealed record WorkspaceRoiSummary(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TotalRunsInPeriod,
    int SuccessfulRuns,
    long TotalTimeSavedMinutes,
    decimal TotalCostAvoided,
    decimal RoiPercentage,
    decimal AvgCostPerRun,
    IReadOnlyList<WorkflowRoiDetail> ByWorkflow);

public sealed record WorkflowRoiDetail(
    Guid WorkflowDefinitionId,
    string WorkflowName,
    bool Configured,
    int Runs,
    int SuccessfulRuns,
    long TimeSavedMinutes,
    decimal CostAvoided,
    decimal RoiPercentage,
    string Currency);

/// <summary>Admin-set ROI baseline (read/write).</summary>
public sealed record RoiConfigDto(
    int ManualProcessTimeMinutes,
    decimal ManualProcessCostPerRunUsd,
    decimal AutomationCostPerRunUsd,
    string MonthlyCurrency);
