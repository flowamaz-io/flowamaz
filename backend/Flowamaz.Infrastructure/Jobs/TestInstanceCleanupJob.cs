using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (daily) that soft-deletes expired test instances.
/// Test instances have a 24-hour TTL set at trigger time.
/// </summary>
[DisallowConcurrentExecution]
public sealed class TestInstanceCleanupJob : IJob
{
    private readonly IWorkflowInstanceRepository _instances;
    private readonly ILogger<TestInstanceCleanupJob> _logger;

    public TestInstanceCleanupJob(IWorkflowInstanceRepository instances, ILogger<TestInstanceCleanupJob> logger)
    {
        _instances = instances;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("TestInstanceCleanupJob enter");
        try
        {
            var deleted = await _instances.SoftDeleteExpiredTestInstancesAsync(DateTime.UtcNow, context.CancellationToken);
            if (deleted > 0)
                _logger.LogInformation("TestInstanceCleanupJob soft-deleted {Count} expired test instances", deleted);
            _logger.LogInformation("TestInstanceCleanupJob exit");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "TestInstanceCleanupJob error while cleaning up expired test instances");
        }
    }
}
