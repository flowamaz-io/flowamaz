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
