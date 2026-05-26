using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Analytics;

/// <summary>
/// Builds the Workflow Weather executive view. Status is computed server-side from failures, SLA
/// compliance and live insight severity (FUNCTIONAL.md §11.6). <see cref="ComputeStatus"/> is pure
/// and unit-tested.
/// </summary>
public sealed class WorkflowWeatherService
{
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly IWorkflowInsightRepository _insights;
    private readonly IWorkflowAnalyticsRepository _analytics;
    private readonly ILogger<WorkflowWeatherService> _logger;

    public WorkflowWeatherService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowMetricRepository metrics,
        IWorkflowInsightRepository insights,
        IWorkflowAnalyticsRepository analytics,
        ILogger<WorkflowWeatherService> logger)
    {
        _definitions = definitions;
        _metrics = metrics;
        _insights = insights;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<WeatherResponse> GetWeatherAsync(Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowWeatherService.GetWeatherAsync enter workspace={WorkspaceId}", workspaceId);

        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var definitions = await _definitions.GetForWorkspaceAsync(workspaceId, ct);
        var latestMetrics = (await _metrics.GetLatestPerWorkflowAsync(workspaceId, ct))
            .ToDictionary(m => m.WorkflowDefinitionId);
        var openInsights = await _insights.GetUnacknowledgedForWorkspaceAsync(workspaceId, ct);
        var insightsByWorkflow = openInsights.GroupBy(i => i.WorkflowDefinitionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var cards = new List<WorkflowWeather>(definitions.Count);
        var totalActive = 0;

        foreach (var def in definitions)
        {
            var metric = latestMetrics.GetValueOrDefault(def.Id);
            var active = await _analytics.GetActiveInstanceCountAsync(workspaceId, def.Id, ct);
            var failedLastHour = await _analytics.GetFailedCountSinceAsync(workspaceId, def.Id, hourAgo, ct);
            var workflowInsights = insightsByWorkflow.GetValueOrDefault(def.Id) ?? [];

            var slaCompliance = SlaCompliancePct(metric);
            var hasCritical = workflowInsights.Any(i => i.Severity == InsightSeverity.Critical);
            var hasWarning = workflowInsights.Any(i => i.Severity == InsightSeverity.Warning);
            var status = ComputeStatus(failedLastHour, slaCompliance, hasCritical, hasWarning);

            totalActive += active;
            cards.Add(new WorkflowWeather(
                def.Id, def.Name, status, active, failedLastHour, slaCompliance,
                workflowInsights.Count,
                workflowInsights.OrderByDescending(i => i.CreatedAt).FirstOrDefault()?.Message));
        }

        var runsThisMonth = await _metrics.GetRunCountSinceAsync(workspaceId, monthStart, ct);

        _logger.LogInformation("WorkflowWeatherService.GetWeatherAsync exit workspace={WorkspaceId} workflows={Count}", workspaceId, cards.Count);
        return new WeatherResponse(now, definitions.Count, totalActive, runsThisMonth, cards);
    }

    /// <summary>
    /// Pure status band (FUNCTIONAL.md §11.6), evaluated worst-first:
    /// red &lt; orange &lt; yellow &lt; green.
    /// </summary>
    public static string ComputeStatus(int failedLastHour, int slaCompliancePct, bool hasCritical, bool hasWarning)
    {
        if (slaCompliancePct < 60 || hasCritical || failedLastHour >= 3) return "red";
        if (slaCompliancePct < 80 || failedLastHour >= 1) return "orange";
        if (slaCompliancePct < 95 || hasWarning) return "yellow";
        return "green";
    }

    private static int SlaCompliancePct(WorkflowMetric? metric)
    {
        if (metric is null || metric.RunsTotal == 0) return 100;
        var compliant = metric.RunsTotal - metric.SlaBreachCount;
        return (int)Math.Round(Math.Clamp(compliant / (double)metric.RunsTotal, 0, 1) * 100);
    }
}
