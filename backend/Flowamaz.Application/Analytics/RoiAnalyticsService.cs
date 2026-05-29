using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Analytics;

/// <summary>
/// Deterministic ROI calculation. Time saved = manual minutes × successful runs; cost avoided =
/// (manual − automation) cost × successful runs; ROI % = cost avoided ÷ total automation cost × 100.
/// </summary>
public sealed class RoiAnalyticsService : IRoiAnalyticsService
{
    private readonly IWorkflowRoiConfigRepository _configs;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RoiAnalyticsService> _logger;

    public RoiAnalyticsService(
        IWorkflowRoiConfigRepository configs,
        IWorkflowMetricRepository metrics,
        IWorkflowDefinitionRepository definitions,
        IUnitOfWork unitOfWork,
        ILogger<RoiAnalyticsService> logger)
    {
        _configs = configs;
        _metrics = metrics;
        _definitions = definitions;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<WorkspaceRoiSummary> GetWorkspaceRoiAsync(Guid workspaceId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default)
    {
        _logger.LogDebug("RoiAnalyticsService.GetWorkspaceRoiAsync enter workspace={WorkspaceId} from={From} to={To}", workspaceId, periodStart, periodEnd);
        try
        {
            var metrics = await _metrics.GetForWorkspaceInRangeAsync(workspaceId, periodStart, periodEnd, ct);
            var configs = (await _configs.GetForWorkspaceAsync(workspaceId, ct)).ToDictionary(c => c.WorkflowDefinitionId);
            var names = (await _definitions.GetForWorkspaceAsync(workspaceId, ct)).ToDictionary(d => d.Id, d => d.Name);

            // Group run counts per workflow over the period.
            var perWorkflow = metrics
                .GroupBy(m => m.WorkflowDefinitionId)
                .ToDictionary(g => g.Key, g => (Runs: g.Sum(m => m.RunsTotal), Successful: g.Sum(m => m.RunsCompleted)));

            // Every workflow that either ran in the period or has a config is reported.
            var workflowIds = perWorkflow.Keys.Union(configs.Keys).ToList();
            var details = new List<WorkflowRoiDetail>(workflowIds.Count);
            foreach (var id in workflowIds)
            {
                perWorkflow.TryGetValue(id, out var counts);
                configs.TryGetValue(id, out var config);
                names.TryGetValue(id, out var name);
                details.Add(BuildDetail(id, name ?? "(deleted workflow)", counts.Runs, counts.Successful, config));
            }

            details = details.OrderByDescending(d => d.CostAvoided).ToList();

            var totalRuns = details.Sum(d => d.Runs);
            var totalSuccessful = details.Sum(d => d.SuccessfulRuns);
            var totalTimeSaved = details.Sum(d => d.TimeSavedMinutes);
            var totalCostAvoided = details.Sum(d => d.CostAvoided);

            // Total automation cost across configured workflows that ran — denominator for ROI %.
            decimal totalAutomationCost = 0m;
            foreach (var id in configs.Keys)
            {
                if (perWorkflow.TryGetValue(id, out var c))
                    totalAutomationCost += configs[id].AutomationCostPerRunUsd * c.Successful;
            }

            var roiPct = totalAutomationCost > 0 ? Math.Round(totalCostAvoided / totalAutomationCost * 100m, 2) : 0m;
            var avgCostPerRun = totalSuccessful > 0 ? Math.Round(totalAutomationCost / totalSuccessful, 2) : 0m;

            _logger.LogInformation("RoiAnalyticsService.GetWorkspaceRoiAsync exit workspace={WorkspaceId} runs={Runs} costAvoided={CostAvoided}", workspaceId, totalRuns, totalCostAvoided);
            return new WorkspaceRoiSummary(periodStart, periodEnd, totalRuns, totalSuccessful, totalTimeSaved, totalCostAvoided, roiPct, avgCostPerRun, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RoiAnalyticsService.GetWorkspaceRoiAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task<WorkflowRoiDetail?> GetWorkflowRoiAsync(Guid workspaceId, Guid workflowId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default)
    {
        var definition = await _definitions.GetByIdForWorkspaceAsync(workflowId, workspaceId, ct);
        if (definition is null) return null;

        var metrics = await _metrics.GetForWorkspaceInRangeAsync(workspaceId, periodStart, periodEnd, ct);
        var forWorkflow = metrics.Where(m => m.WorkflowDefinitionId == workflowId).ToList();
        var config = await _configs.GetForWorkflowAsync(workflowId, workspaceId, ct);

        return BuildDetail(workflowId, definition.Name, forWorkflow.Sum(m => m.RunsTotal), forWorkflow.Sum(m => m.RunsCompleted), config);
    }

    public async Task<RoiConfigDto?> GetConfigAsync(Guid workspaceId, Guid workflowId, CancellationToken ct = default)
    {
        var config = await _configs.GetForWorkflowAsync(workflowId, workspaceId, ct);
        return config is null ? null : ToDto(config);
    }

    public async Task<RoiConfigDto?> UpsertConfigAsync(Guid workspaceId, Guid workflowId, RoiConfigDto config, CancellationToken ct = default)
    {
        _logger.LogDebug("RoiAnalyticsService.UpsertConfigAsync enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
        try
        {
            var definition = await _definitions.GetByIdForWorkspaceAsync(workflowId, workspaceId, ct);
            if (definition is null) return null;

            var existing = await _configs.GetForWorkflowAsync(workflowId, workspaceId, ct);
            if (existing is null)
            {
                existing = new WorkflowRoiConfig
                {
                    WorkspaceId = workspaceId,
                    WorkflowDefinitionId = workflowId,
                    ManualProcessTimeMinutes = config.ManualProcessTimeMinutes,
                    ManualProcessCostPerRunUsd = config.ManualProcessCostPerRunUsd,
                    AutomationCostPerRunUsd = config.AutomationCostPerRunUsd,
                    MonthlyCurrency = string.IsNullOrWhiteSpace(config.MonthlyCurrency) ? "MYR" : config.MonthlyCurrency,
                };
                await _configs.AddAsync(existing, ct);
            }
            else
            {
                existing.ManualProcessTimeMinutes = config.ManualProcessTimeMinutes;
                existing.ManualProcessCostPerRunUsd = config.ManualProcessCostPerRunUsd;
                existing.AutomationCostPerRunUsd = config.AutomationCostPerRunUsd;
                existing.MonthlyCurrency = string.IsNullOrWhiteSpace(config.MonthlyCurrency) ? "MYR" : config.MonthlyCurrency;
                _configs.Update(existing);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("RoiAnalyticsService.UpsertConfigAsync exit workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
            return ToDto(existing);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RoiAnalyticsService.UpsertConfigAsync error workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowId);
            throw;
        }
    }

    private static WorkflowRoiDetail BuildDetail(Guid workflowId, string name, int runs, int successful, WorkflowRoiConfig? config)
    {
        if (config is null)
            return new WorkflowRoiDetail(workflowId, name, false, runs, successful, 0, 0m, 0m, "MYR");

        var timeSaved = (long)config.ManualProcessTimeMinutes * successful;
        var costAvoided = (config.ManualProcessCostPerRunUsd - config.AutomationCostPerRunUsd) * successful;
        var automationTotal = config.AutomationCostPerRunUsd * successful;
        var roiPct = automationTotal > 0 ? Math.Round(costAvoided / automationTotal * 100m, 2) : 0m;

        return new WorkflowRoiDetail(workflowId, name, true, runs, successful, timeSaved, Math.Round(costAvoided, 2), roiPct, config.MonthlyCurrency);
    }

    private static RoiConfigDto ToDto(WorkflowRoiConfig c) =>
        new(c.ManualProcessTimeMinutes, c.ManualProcessCostPerRunUsd, c.AutomationCostPerRunUsd, c.MonthlyCurrency);
}
