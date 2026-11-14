using Flowamaz.Application.Workflow.Twin;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Workflow Twin background simulation (FUNCTIONAL.md §8.1: "every live workflow automatically runs silent shadow
/// simulations"). Hourly, it simulates up to <see cref="BatchSize"/> workflows whose production version changed or whose
/// last Twin run is more than a day old. Simulation is in memory with no side effects and no AI cost.
/// </summary>
[DisallowConcurrentExecution]
public sealed class WorkflowTwinJob : IJob
{
    public const int BatchSize = 25;

    private readonly WorkflowTwinService _twin;
    private readonly ILogger<WorkflowTwinJob> _logger;

    public WorkflowTwinJob(WorkflowTwinService twin, ILogger<WorkflowTwinJob> logger)
    {
        _twin = twin;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.LogInformation("WorkflowTwinJob starting");
            var done = await _twin.RunDueAsync(BatchSize, context.CancellationToken);
            _logger.LogInformation("WorkflowTwinJob finished simulated={Count}", done);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WorkflowTwinJob error");
        }
    }
}
