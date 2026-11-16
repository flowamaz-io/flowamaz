using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flowamaz.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build <see cref="EngineDbContext"/> to add or apply the engine's migrations.
/// The connection string comes from <c>FLOWAMAZ_ENGINE_DB</c>; adding a migration doesn't connect.
/// </summary>
public sealed class EngineDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EngineDbContext>
{
    public EngineDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWAMAZ_ENGINE_DB")
            ?? "Host=localhost;Port=5432;Database=flowamaz_engine;Username=postgres";
        var options = new DbContextOptionsBuilder<EngineDbContext>()
            .UseNpgsql(connectionString, EngineDbContextOptions.ConfigureNpgsql)
            .Options;
        return new EngineDbContext(options);
    }
}

/// <summary>Npgsql settings a standalone host should use with <see cref="EngineDbContext"/>.</summary>
public static class EngineDbContextOptions
{
    /// <summary>The migrations history table, the same name the Flowamaz product uses.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static void ConfigureNpgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(MigrationsHistoryTable)
              .MigrationsAssembly(typeof(EngineDbContext).Assembly.GetName().Name);
}
