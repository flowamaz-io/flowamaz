using Microsoft.Extensions.Configuration;

namespace Flowamaz.Infrastructure;

/// <summary>
/// Single source of truth for resolving the Postgres and Redis connection strings.
/// The documented single-name env vars (DB_CONNECTION_STRING / REDIS_CONNECTION_STRING) win;
/// the standard binder keys (ConnectionStrings:DefaultConnection / ConnectionStrings:Redis,
/// also settable via ConnectionStrings__*) are the fallback. Used by both the Infrastructure
/// composition root and the API health checks so the two never drift.
/// </summary>
public static class ConnectionStringResolver
{
    public static string ResolveDatabase(IConfiguration configuration) =>
        configuration["DB_CONNECTION_STRING"]
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Database connection string is required (set DB_CONNECTION_STRING or ConnectionStrings__DefaultConnection).");

    public static string ResolveRedis(IConfiguration configuration) =>
        configuration["REDIS_CONNECTION_STRING"]
        ?? configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException(
            "Redis connection string is required (set REDIS_CONNECTION_STRING or ConnectionStrings__Redis).");
}
