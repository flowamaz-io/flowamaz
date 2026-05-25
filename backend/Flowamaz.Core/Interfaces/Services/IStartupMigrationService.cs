namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Applies pending EF Core migrations on application startup so a fresh <c>docker compose up</c>
/// self-bootstraps its schema with no manual <c>dotnet ef database update</c>. Called once from
/// Program.cs before <c>app.Run()</c>.
/// </summary>
public interface IStartupMigrationService
{
    /// <summary>
    /// Applies all pending migrations. No-op when the schema is current. Re-throws on failure —
    /// the app must not start against a bad schema. Bounded by a configurable timeout.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
