using Flowamaz.Core.Interfaces.Library;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// The Library GitHub sync's polling fallback (every 15 minutes, FUNCTIONAL.md §23.7); the push webhook triggers
/// it straight away. One run at a time.
/// </summary>
[DisallowConcurrentExecution]
public sealed class LibrarySyncJob : IJob
{
    private readonly ILibrarySyncService _sync;
    private readonly ILogger<LibrarySyncJob> _log;

    public LibrarySyncJob(ILibrarySyncService sync, ILogger<LibrarySyncJob> log)
    {
        _sync = sync;
        _log = log;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _log.LogInformation("LibrarySyncJob.Execute entry");
        try
        {
            var reports = await _sync.SyncAsync(context.CancellationToken);
            _log.LogInformation("LibrarySyncJob.Execute exit {Reports}", string.Join("; ", reports.Select(r => $"{r.Source}={r.Outcome}")));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SyncAsync records per-source failures itself; this is only for something unexpected (the database).
            _log.LogError(ex, "LibrarySyncJob.Execute error");
        }
    }
}
