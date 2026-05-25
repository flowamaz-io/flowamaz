using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Thin seam over EF Core's migration APIs. Exists so <see cref="StartupMigrationService"/> is
/// unit-testable (the no-op / applies / throws paths) without a live database — the timeout and
/// logging policy live in the service, the data access lives here.
/// </summary>
public interface IMigrationRunner
{
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken);
    Task MigrateAsync(CancellationToken cancellationToken);
}

/// <summary>Default <see cref="IMigrationRunner"/> backed by the application DbContext.</summary>
public sealed class EfMigrationRunner(FlowAmazDbContext db) : IMigrationRunner
{
    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken) =>
        (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

    public Task MigrateAsync(CancellationToken cancellationToken) =>
        db.Database.MigrateAsync(cancellationToken);
}

/// <inheritdoc />
public sealed class StartupMigrationService : IStartupMigrationService
{
    private readonly IMigrationRunner _runner;
    private readonly ILogger<StartupMigrationService> _logger;
    private readonly int _timeoutSeconds;

    public StartupMigrationService(
        IMigrationRunner runner,
        IConfiguration configuration,
        ILogger<StartupMigrationService> logger)
    {
        _runner = runner;
        _logger = logger;
        _timeoutSeconds = configuration.GetValue<int?>("StartupMigrationTimeoutSeconds") ?? 120;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "StartupMigrationService.RunAsync enter — timeoutSeconds={TimeoutSeconds}", _timeoutSeconds);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
        var ct = cts.Token;

        try
        {
            var pending = await _runner.GetPendingMigrationsAsync(ct);
            if (pending.Count == 0)
            {
                _logger.LogInformation(
                    "StartupMigrationService.RunAsync exit — schema is current, no pending migrations");
                return;
            }

            _logger.LogInformation("Applying {Count} pending migrations...", pending.Count);
            foreach (var name in pending)
            {
                _logger.LogInformation("  pending migration: {Migration}", name);
            }

            await _runner.MigrateAsync(ct);

            _logger.LogInformation(
                "StartupMigrationService.RunAsync exit — migrations complete ({Count} applied)", pending.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "StartupMigrationService.RunAsync error — migration failed; the app must not start with a bad schema");
            throw;
        }
    }
}
