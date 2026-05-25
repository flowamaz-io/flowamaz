using Flowamaz.Core.Interfaces.Queue;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (every 5s) that promotes delayed-queue items whose execute-after time has passed back
/// onto their workspace pending queue. This is how retry/backoff re-queues become runnable again.
/// </summary>
[DisallowConcurrentExecution]
public sealed class DelayedQueuePromoterJob : IJob
{
    private readonly ITaskQueue _queue;
    private readonly ILogger<DelayedQueuePromoterJob> _logger;

    public DelayedQueuePromoterJob(ITaskQueue queue, ILogger<DelayedQueuePromoterJob> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var ready = await _queue.GetDelayedReadyAsync(context.CancellationToken);
            foreach (var item in ready)
            {
                await _queue.EnqueueAsync(item.WorkspaceId, item.InstanceId, context.CancellationToken);
            }

            if (ready.Count > 0)
            {
                _logger.LogInformation("DelayedQueuePromoterJob promoted {Count} delayed items to pending", ready.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "DelayedQueuePromoterJob error while promoting delayed items");
        }
    }
}
