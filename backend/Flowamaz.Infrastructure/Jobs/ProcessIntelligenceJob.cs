using Flowamaz.Application.Analytics;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Thin Quartz wrapper around <see cref="ProcessIntelligenceService"/> (hourly). Quartz lives in
/// Infrastructure; the analytics logic stays in the Application layer (and stays unit-testable).
/// </summary>
[DisallowConcurrentExecution]
public sealed class ProcessIntelligenceJob : IJob
{
    private readonly ProcessIntelligenceService _service;
    private readonly ILogger<ProcessIntelligenceJob> _logger;

    public ProcessIntelligenceJob(ProcessIntelligenceService service, ILogger<ProcessIntelligenceJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.LogInformation("ProcessIntelligenceJob starting");
            await _service.RunAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ProcessIntelligenceJob error");
        }
    }
}
