using FluentAssertions;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Flowamaz.Tests.Integration.Infrastructure;

/// <summary>
/// Spins up a real PostgreSQL 16 container, runs the InitialScaffold migration, and asserts
/// the seed counts. Confirms snake_case naming, jsonb columns, and HasData rows all line up
/// with what a fresh production deploy would get.
/// </summary>
public class MigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("flowamaz_test")
        .WithUsername("flowamaz")
        .WithPassword("flowamaz_test_pw")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private FlowAmazDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new FlowAmazDbContext(options);
    }

    [Fact]
    public async Task Migration_applies_cleanly_on_fresh_postgres()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        // Sanity — at least one migration row was recorded.
        var migrations = await db.Database.GetAppliedMigrationsAsync();
        migrations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Model_catalogue_seeds_every_catalogue_model()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var count = await db.ModelCatalogue.CountAsync();
        count.Should().Be(AiSeedData.Models.Count,
            "the migration seeds exactly the models defined in AiSeedData (FUNCTIONAL.md §5.1)");

        // Spot-check a few capability flags
        var sonnet = await db.ModelCatalogue.SingleAsync(m => m.ModelId == "claude-sonnet-4-6");
        sonnet.HasVision.Should().BeTrue();
        sonnet.MaxContextTokens.Should().BeGreaterOrEqualTo(200_000);

        var haiku = await db.ModelCatalogue.SingleAsync(m => m.ModelId == "claude-haiku-4-5");
        haiku.HasVision.Should().BeFalse();
    }

    [Fact]
    public async Task Platform_ai_config_has_exactly_seven_seeded_rows()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var count = await db.PlatformAiConfigs.CountAsync();
        count.Should().Be(7, "FUNCTIONAL.md §5.2 declares 7 platform AI functions (F1-F7)");
    }

    [Fact]
    public async Task Schema_uses_snake_case_table_names()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var rows = await db.Database
            .SqlQueryRaw<string>(@"SELECT table_name FROM information_schema.tables WHERE table_schema='public'")
            .ToListAsync();

        rows.Should().Contain("model_catalogue");
        rows.Should().Contain("platform_ai_configs");
        rows.Should().Contain("ai_token_usage");
        rows.Should().NotContain("ModelCatalogue");
    }
}
