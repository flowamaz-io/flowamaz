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
/// Processes pending Anthropic batch requests for F5 (Process Intelligence).
/// Called by <c>BatchResultPollerJob</c> every 30 minutes. For each pending entry it polls the
/// Anthropic Batch API; if the result is ready the AI insights are parsed and stored and the
/// entry is removed from the state store. If still processing the entry is left for the next run.
/// </summary>
public sealed class BatchInsightProcessorService
{
    private static readonly TimeSpan InsightCacheTtl = TimeSpan.FromHours(24);

    private readonly IAiBatchStateService _batchState;
    private readonly IAiCompletionService _completion;
    private readonly IModelResolutionService _modelResolution;
    private readonly IWorkflowInsightRepository _insights;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiTokenMeteringService _metering;
    private readonly ISemanticCacheService _semanticCache;
    private readonly ILogger<BatchInsightProcessorService> _logger;

    public BatchInsightProcessorService(
        IAiBatchStateService batchState,
        IAiCompletionService completion,
        IModelResolutionService modelResolution,
        IWorkflowInsightRepository insights,
        IUnitOfWork unitOfWork,
        IAiTokenMeteringService metering,
        ISemanticCacheService semanticCache,
        ILogger<BatchInsightProcessorService> logger)
    {
        _batchState = batchState;
        _completion = completion;
        _modelResolution = modelResolution;
        _insights = insights;
        _unitOfWork = unitOfWork;
        _metering = metering;
        _semanticCache = semanticCache;
        _logger = logger;
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("BatchInsightProcessorService.ProcessPendingAsync enter");

        var pending = await _batchState.GetAllAsync(cancellationToken);
        if (pending.Count == 0)
        {
            _logger.LogDebug("BatchInsightProcessorService.ProcessPendingAsync no pending batches");
            return;
        }

        var processed = 0;
        foreach (var entry in pending)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (await TryProcessEntryAsync(entry, cancellationToken))
                processed++;
        }

        _logger.LogInformation("BatchInsightProcessorService.ProcessPendingAsync exit processed={Processed} of {Total}", processed, pending.Count);
    }

    private async Task<bool> TryProcessEntryAsync(BatchPendingEntry entry, CancellationToken ct)
    {
        try
        {
            var config = await _modelResolution.ResolveModelConfigAsync(AiFunctionIds.ProcessIntel, entry.WorkspaceId, ct);
            var result = await _completion.PollBatchResultAsync(entry.BatchId, config, ct);

            if (result is null)
            {
                _logger.LogDebug("BatchInsightProcessorService: batch still processing batchId={BatchId}", entry.BatchId);
                return false;
            }

            // Populate semantic cache so future hourly runs for identical metrics skip the AI call
            await _semanticCache.SetAsync(entry.CacheKey, result.Text, InsightCacheTtl, ct);

            _metering.RecordUsage(AiFunctionIds.ProcessIntel, entry.ModelId, entry.Provider,
                orgId: null, workspaceId: entry.WorkspaceId, result.TokensInput, result.TokensOutput, costUsd: 0m);

            foreach (var insight in ParseAiInsights(result.Text, entry.WorkspaceId, entry.WorkflowDefinitionId))
                await _insights.ReplaceUnacknowledgedAsync(insight, ct);

            await _unitOfWork.SaveChangesAsync(ct);
            await _batchState.RemoveAsync(entry.BatchId, ct);

            _logger.LogInformation(
                "BatchInsightProcessorService: processed batchId={BatchId} workspace={WorkspaceId} workflow={WorkflowId} tokensIn={In} tokensOut={Out}",
                entry.BatchId, entry.WorkspaceId, entry.WorkflowDefinitionId, result.TokensInput, result.TokensOutput);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "BatchInsightProcessorService: error processing batchId={BatchId}", entry.BatchId);
            return false;
        }
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
            // Model didn't return a JSON array — no AI insights recorded.
        }

        return result;
    }
}
