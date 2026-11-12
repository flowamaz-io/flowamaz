using Flowamaz.Application.Auth.Scim;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (every 10s) that re-applies workspace roles for people queued by large SCIM group changes
/// (<see cref="ScimService.InlineResyncLimit"/>). Works through up to 40 batches of 50 per run (small transactions,
/// so SCIM requests aren't held up), so a group of 2,000 people is done in one run and a much larger one within minutes.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ScimRoleResyncJob : IJob
{
    private const int BatchSize = 50;
    private const int MaxBatchesPerRun = 40;

    private readonly ScimService _scim;
    private readonly ILogger<ScimRoleResyncJob> _logger;

    public ScimRoleResyncJob(ScimService scim, ILogger<ScimRoleResyncJob> logger)
    {
        _scim = scim;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogDebug("ScimRoleResyncJob.Execute enter");
        var total = 0;
        try
        {
            for (var i = 0; i < MaxBatchesPerRun; i++)
            {
                var processed = await _scim.ProcessQueuedRoleResyncsAsync(BatchSize, context.CancellationToken);
                total += processed;
                if (processed < BatchSize) break;
            }
            if (total > 0) _logger.LogInformation("ScimRoleResyncJob.Execute exit processed={Count}", total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ProcessQueuedRoleResyncsAsync handles a failed batch itself; this is anything unexpected around it.
            _logger.LogError(ex, "ScimRoleResyncJob.Execute error processed={Count}", total);
        }
    }
}
