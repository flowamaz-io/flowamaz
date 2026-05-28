using Flowamaz.Application.Analytics;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (every 30 minutes) that polls pending Anthropic batch requests for F5 insights
/// and processes any that have completed. The analytics logic lives in
/// <see cref="BatchInsightProcessorService"/> (Application layer, unit-testable).
/// </summary>
[DisallowConcurrentExecution]
public sealed class BatchResultPollerJob : IJob
{
    private readonly BatchInsightProcessorService _service;
    private readonly ILogger<BatchResultPollerJob> _logger;

    public BatchResultPollerJob(BatchInsightProcessorService service, ILogger<BatchResultPollerJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.LogInformation("BatchResultPollerJob starting");
            await _service.ProcessPendingAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "BatchResultPollerJob error");
        }
    }
}
