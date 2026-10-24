using Flowamaz.Application.Workflow.Scheduling;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (every minute) that fires published workflows whose trigger is a schedule.
///
/// For each scheduled workflow it finds the latest cron occurrence inside a short catch-up window
/// and triggers one run with idempotency key <c>schedule:{definitionId}:{occurrence}</c>. The unique
/// index on idempotency_key makes this exactly-once even when several API instances run the job or
/// the job overlaps a restart. Missed occurrences older than the window are skipped (no backlog storm).
/// </summary>
[DisallowConcurrentExecution]
public sealed class ScheduleTriggerJob : IJob
{
    /// <summary>How far back an occurrence may be and still fire (covers a missed tick or a short restart).</summary>
    public static readonly TimeSpan CatchUpWindow = TimeSpan.FromMinutes(5);

    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly ILogger<ScheduleTriggerJob> _logger;
    private readonly TimeProvider _clock;

    public ScheduleTriggerJob(
        IWorkflowDefinitionRepository definitions,
        IWorkflowVersionRepository versions,
        IWorkflowOrchestrator orchestrator,
        ILogger<ScheduleTriggerJob> logger,
        TimeProvider? clock = null)
    {
        _definitions = definitions;
        _versions = versions;
        _orchestrator = orchestrator;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task Execute(IJobExecutionContext context) => await RunOnceAsync(context.CancellationToken);

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        _logger.LogDebug("ScheduleTriggerJob.RunOnceAsync enter");
        var fired = 0;
        var now = _clock.GetUtcNow().UtcDateTime;

        List<Core.Entities.Workflow.WorkflowDefinition> scheduled;
        try
        {
            scheduled = await _definitions.GetPublishedScheduledAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ScheduleTriggerJob: could not load scheduled workflows");
            return 0;
        }

        foreach (var definition in scheduled)
        {
            try
            {
                var version = await _versions.GetProductionAsync(definition.Id, definition.WorkspaceId, ct);
                if (version is null) continue;

                var trigger = WorkflowTriggerReader.Read(version.YamlContent);
                if (trigger.Type != WorkflowTriggerType.Schedule) continue;
                if (!CronSchedule.TryParse(trigger.Cron, trigger.TimeZone, out var parsed, out var error))
                {
                    _logger.LogWarning("ScheduleTriggerJob: workflow={WorkflowId} has an invalid schedule: {Error}", definition.Id, error);
                    continue;
                }

                var occurrence = parsed!.Expression
                    .GetOccurrences(now - CatchUpWindow, now, parsed.TimeZone, fromInclusive: false, toInclusive: true)
                    .LastOrDefault();
                if (occurrence == default) continue;

                var key = $"schedule:{definition.Id:N}:{occurrence:yyyyMMddHHmmss}";
                var payload = System.Text.Json.JsonSerializer.Serialize(new { scheduled_at = occurrence.ToString("O"), cron = trigger.Cron });
                await _orchestrator.TriggerAsync(definition.WorkspaceId, definition.Id, payload, key, InstanceTriggerType.Schedule,
                    cancellationToken: ct);
                fired++;
                _logger.LogInformation("ScheduleTriggerJob: fired workflow={WorkflowId} occurrence={Occurrence}", definition.Id, occurrence);
            }
            catch (DbUpdateException)
            {
                // Another instance fired this occurrence first (unique idempotency key) — already done.
            }
            catch (Exception ex) when (ex is AppException)
            {
                // Archived workspace, plan/edition limit, etc. Skip this occurrence, keep the others running.
                _logger.LogWarning("ScheduleTriggerJob: workflow={WorkflowId} not started: {Message}", definition.Id, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ScheduleTriggerJob: error firing workflow={WorkflowId}", definition.Id);
            }
        }

        _logger.LogDebug("ScheduleTriggerJob.RunOnceAsync exit fired={Fired}", fired);
        return fired;
    }
}
