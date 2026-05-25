using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (every 15s) that recovers orphaned instances — Running rows whose worker lease has
/// expired because a worker died without releasing. It re-queues them so another worker can claim
/// them (the new worker's <c>TryAcquireLease</c> succeeds because the old lease is expired).
/// </summary>
[DisallowConcurrentExecution]
public sealed class WorkerLeaseExpiryJob : IJob
{
    private readonly IWorkflowInstanceRepository _instances;
    private readonly ITaskQueue _queue;
    private readonly ILogger<WorkerLeaseExpiryJob> _logger;

    public WorkerLeaseExpiryJob(
        IWorkflowInstanceRepository instances,
        ITaskQueue queue,
        ILogger<WorkerLeaseExpiryJob> logger)
    {
        _instances = instances;
        _queue = queue;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var orphaned = await _instances.GetOrphanedAsync(DateTime.UtcNow, context.CancellationToken);
            foreach (var instance in orphaned)
            {
                await _queue.EnqueueAsync(instance.WorkspaceId, instance.Id, context.CancellationToken);
                _logger.LogWarning(
                    "WorkerLeaseExpiryJob recovered orphaned instance={InstanceId} (lease expired at {ExpiresAt}) — re-queued",
                    instance.Id, instance.WorkerLeaseExpiresAt);
            }

            if (orphaned.Count > 0)
            {
                _logger.LogInformation("WorkerLeaseExpiryJob re-queued {Count} orphaned instances", orphaned.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WorkerLeaseExpiryJob error while scanning for orphaned instances");
        }
    }
}
