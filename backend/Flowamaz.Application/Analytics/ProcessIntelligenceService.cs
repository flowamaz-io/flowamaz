using System.Globalization;
using System.Text.Json;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Analytics;

/// <summary>
/// The hourly Process Intelligence pass (FUNCTIONAL.md §11.2). Computes the last-hour metric for
/// every workflow with recent runs, raises deterministic SLA-risk insights, and (best-effort) asks
/// F5 for additional insights — semantically cached so identical metrics don't re-bill. Hosted as a
/// Quartz job by a thin Infrastructure wrapper; the analytics logic lives here so it stays testable.
/// </summary>
public sealed class ProcessIntelligenceService
{
    private static readonly TimeSpan InsightCacheTtl = TimeSpan.FromHours(24);

    private readonly IWorkflowAnalyticsRepository _analytics;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly IWorkflowInsightRepository _insights;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IModelResolutionService _modelResolution;
    private readonly IAiCompletionService _completion;
    private readonly IAiTokenMeteringService _metering;
    private readonly ISemanticCacheService _semanticCache;
    private readonly ILogger<ProcessIntelligenceService> _logger;

    public ProcessIntelligenceService(
        IWorkflowAnalyticsRepository analytics,
        IWorkflowMetricRepository metrics,
        IWorkflowInsightRepository insights,
        IUnitOfWork unitOfWork,
        IModelResolutionService modelResolution,
        IAiCompletionService completion,
        IAiTokenMeteringService metering,
        ISemanticCacheService semanticCache,
        ILogger<ProcessIntelligenceService> logger)
    {
        _analytics = analytics;
        _metrics = metrics;
        _insights = insights;
        _unitOfWork = unitOfWork;
        _modelResolution = modelResolution;
        _completion = completion;
        _metering = metering;
        _semanticCache = semanticCache;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var periodStart = hourStart.AddHours(-1);
        var since24h = now.AddHours(-24);

        _logger.LogInformation("ProcessIntelligenceService.RunAsync enter period={PeriodStart:o}", periodStart);

        var workspaces = await _analytics.GetWorkspacesWithRunsSinceAsync(since24h, cancellationToken);
        var processed = 0;
        foreach (var workspaceId in workspaces)
        {
            var definitionIds = await _analytics.GetWorkflowIdsWithRunsSinceAsync(workspaceId, since24h, cancellationToken);
            foreach (var definitionId in definitionIds)
            {
                await ProcessWorkflowAsync(workspaceId, definitionId, periodStart, hourStart, cancellationToken);
                processed++;
            }
        }

        _logger.LogInformation("ProcessIntelligenceService.RunAsync exit — processed {Count} workflows across {Workspaces} workspaces",
            processed, workspaces.Count);
    }

    private async Task ProcessWorkflowAsync(Guid workspaceId, Guid definitionId, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        var durations = await _analytics.GetDurationsAsync(workspaceId, definitionId, periodStart, periodEnd, ct);
        var bottleneck = await _analytics.GetBottleneckAsync(workspaceId, definitionId, periodStart, periodEnd, ct);
        var metric = BuildMetric(workspaceId, definitionId, periodStart, durations, bottleneck);
        await _metrics.UpsertAsync(metric, ct);

        var slaMessage = EvaluateSlaRisk(definitionId.ToString(), metric.AvgDurationMs, metric.SlaThresholdMs);
        if (slaMessage is not null)
        {
            await _insights.ReplaceUnacknowledgedAsync(new WorkflowInsight
            {
                WorkspaceId = workspaceId,
                WorkflowDefinitionId = definitionId,
                InsightType = InsightType.SlaRisk,
                Severity = InsightSeverity.Warning,
                Message = slaMessage,
                Data = JsonSerializer.Serialize(new { avgMs = metric.AvgDurationMs, slaMs = metric.SlaThresholdMs }),
            }, ct);
        }

        await GenerateAiInsightsAsync(workspaceId, definitionId, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task GenerateAiInsightsAsync(Guid workspaceId, Guid definitionId, CancellationToken ct)
    {
        var history = await _metrics.GetForDefinitionSinceAsync(definitionId, workspaceId, DateTime.UtcNow.AddDays(-7), ct);
        if (history.Count == 0) return;

        var payload = JsonSerializer.Serialize(history.Select(m => new
        {
            hour = m.PeriodHour,
            m.RunsTotal,
            m.RunsFailed,
            m.AvgDurationMs,
            m.P95DurationMs,
            m.BottleneckNodeId,
        }));

        var cacheKey = _semanticCache.ComputeKey(AiFunctionIds.ProcessIntel, workspaceId, payload);
        var cached = await _semanticCache.GetAsync(cacheKey, ct);

        string aiJson;
        if (cached is not null)
        {
            aiJson = cached;
        }
        else
        {
            const string systemPrompt =
                "You are a workflow analytics assistant. Given workflow metrics, identify actionable insights. " +
                "Return JSON only: [{\"type\":\"Bottleneck|AnomalyDetected|CompletionForecast|PatternChange\",\"severity\":\"Info|Warning|Critical\",\"message\":\"...\",\"data\":{}}]";
            var config = await _modelResolution.ResolveModelConfigAsync(AiFunctionIds.ProcessIntel, workspaceId, ct);
            var result = await _completion.CompleteAsync(config, systemPrompt, payload, ct);
            aiJson = result.Text;
            _metering.RecordUsage(AiFunctionIds.ProcessIntel, config.ModelId, config.Provider,
                orgId: null, workspaceId: workspaceId, result.TokensInput, result.TokensOutput, costUsd: 0m);
            await _semanticCache.SetAsync(cacheKey, aiJson, InsightCacheTtl, ct);
        }

        foreach (var insight in ParseAiInsights(aiJson, workspaceId, definitionId))
        {
            await _insights.ReplaceUnacknowledgedAsync(insight, ct);
        }
    }

    /// <summary>
    /// Deterministic SLA-risk check (no AI). Fires when the average run time exceeds 80% of the SLA
    /// threshold; returns the actionable message, or null when there is no risk / no threshold.
    /// </summary>
    public static string? EvaluateSlaRisk(string workflowName, long avgDurationMs, long? slaThresholdMs)
    {
        if (slaThresholdMs is null or <= 0) return null;
        if (avgDurationMs <= slaThresholdMs.Value * 0.8) return null;

        var pct = (int)Math.Round(avgDurationMs / (double)slaThresholdMs.Value * 100);
        var avgHours = (avgDurationMs / 3_600_000.0).ToString("0.0", CultureInfo.InvariantCulture);
        var slaHours = (slaThresholdMs.Value / 3_600_000.0).ToString("0.0", CultureInfo.InvariantCulture);
        return $"Workflow {workflowName} is averaging {avgHours}h — {pct}% of your {slaHours}h SLA. " +
               "At the current rate the SLA will breach soon; investigate the slowest node.";
    }

    public static WorkflowMetric BuildMetric(
        Guid workspaceId, Guid definitionId, DateTime periodHour, List<InstanceDurationSample> samples, BottleneckSample? bottleneck)
    {
        var completedDurations = samples
            .Where(s => s.Status == InstanceStatus.Completed && s.DurationMs is not null)
            .Select(s => s.DurationMs!.Value)
            .OrderBy(ms => ms)
            .ToList();

        return new WorkflowMetric
        {
            WorkspaceId = workspaceId,
            WorkflowDefinitionId = definitionId,
            PeriodHour = periodHour,
            RunsTotal = samples.Count,
            RunsCompleted = samples.Count(s => s.Status == InstanceStatus.Completed),
            RunsFailed = samples.Count(s => s.Status == InstanceStatus.Failed),
            RunsCancelled = samples.Count(s => s.Status == InstanceStatus.Cancelled),
            AvgDurationMs = completedDurations.Count > 0 ? (long)completedDurations.Average() : 0,
            P95DurationMs = Percentile(completedDurations, 95),
            P99DurationMs = Percentile(completedDurations, 99),
            SlaThresholdMs = null, // no per-workflow SLA source yet (future); breach count stays 0
            SlaBreachCount = 0,
            BottleneckNodeId = bottleneck?.NodeId,
            BottleneckAvgMs = bottleneck?.AvgMs,
        };
    }

    public static long Percentile(List<long> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 0) return 0;
        var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Count) - 1;
        return sortedAscending[Math.Clamp(rank, 0, sortedAscending.Count - 1)];
    }

    private static IEnumerable<WorkflowInsight> ParseAiInsights(string json, Guid workspaceId, Guid definitionId)
    {
        var result = new List<WorkflowInsight>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.String) continue;
                var type = element.TryGetProperty("type", out var t) && Enum.TryParse<InsightType>(t.GetString(), true, out var pt)
                    ? pt : InsightType.PatternChange;
                var severity = element.TryGetProperty("severity", out var s) && Enum.TryParse<InsightSeverity>(s.GetString(), true, out var ps)
                    ? ps : InsightSeverity.Info;
                var data = element.TryGetProperty("data", out var d) ? d.GetRawText() : "{}";

                result.Add(new WorkflowInsight
                {
                    WorkspaceId = workspaceId,
                    WorkflowDefinitionId = definitionId,
                    InsightType = type,
                    Severity = severity,
                    Message = msg.GetString()!,
                    Data = data,
                });
            }
        }
        catch (JsonException)
        {
            // The model didn't return a JSON array (e.g. the Phase-2 local completion) — no AI insights.
        }

        return result;
    }
}
