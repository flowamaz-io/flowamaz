using Flowamaz.Core.Interfaces.Queue;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Queue;

/// <summary>
/// Moves runs whose retry backoff has ended from the queue's delayed set back to pending, once a second
/// (the same job the Flowamaz product runs on Quartz). Without it a retry would wait forever.
/// </summary>
public sealed class DelayedQueuePromoterService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private readonly ITaskQueue _queue;
    private readonly ILogger<DelayedQueuePromoterService> _logger;

    public DelayedQueuePromoterService(ITaskQueue queue, ILogger<DelayedQueuePromoterService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    /// <summary>One pass: due runs back to pending. Returns how many moved.</summary>
    public async Task<int> PromoteDueAsync(CancellationToken cancellationToken)
    {
        var ready = await _queue.GetDelayedReadyAsync(cancellationToken);
        foreach (var item in ready)
        {
            await _queue.EnqueueAsync(item.WorkspaceId, item.InstanceId, cancellationToken);
        }
        if (ready.Count > 0) _logger.LogInformation("DelayedQueuePromoterService promoted {Count} delayed runs", ready.Count);
        return ready.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PromoteDueAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DelayedQueuePromoterService error while promoting delayed runs");
                await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }
}
