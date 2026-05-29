using Flowamaz.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Flowamaz.Tests.Integration.Common;

/// <summary>
/// Boots the real API once against throwaway Postgres + Redis containers (migrations applied) and
/// shares it across the whole "api" test collection. Persistence/cache are swapped to the
/// containers via <c>ConfigureTestServices</c> (in-memory config does not reliably override
/// appsettings under WebApplicationFactory).
/// </summary>
public sealed class IntegrationApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine").WithDatabase("flowamaz_api_test")
        .WithUsername("flowamaz").WithPassword("flowamaz_test_pw").Build();

    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();

    private WebApplicationFactory<Flowamaz.Api.Program> _factory = null!;
    private ConnectionMultiplexer _redisClient = null!;
    private string _gitReposPath = null!;

    /// <summary>Fixed signing key used for gate HMAC in integration tests. Known value so tests can reproduce signatures.</summary>
    public const string GateSigningKey = "integration-test-gate-signing-key-32!!";

    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        // Disable the background worker + Quartz jobs for tests — the live polling loop would race
        // API writes (sequence numbers / node states). The full execution loop is exercised in 02-08.
        Environment.SetEnvironmentVariable("Worker__Enabled", "false");
        // A dummy platform key so F5 model resolution succeeds for the interpreter; UseStubCompletion
        // keeps AiCompletionService on the local stub so the dummy key never drives a live call.
        Environment.SetEnvironmentVariable("Ai__AnthropicPlatformKey", "test-platform-key");
        Environment.SetEnvironmentVariable("Ai__UseStubCompletion", "true");
        // Known gate signing key so integration tests can reproduce HMAC signatures.
        Environment.SetEnvironmentVariable("GATE_SIGNING_KEY", GateSigningKey);
        // Isolate per-workspace Git repos to a throwaway temp dir (the appsettings default is the
        // Docker prod path /app/data/repos, which is not writable on the test host).
        _gitReposPath = Path.Combine(Path.GetTempPath(), "flowamaz-it-git", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("Git__ReposBasePath", _gitReposPath);
        // Run integration tests as a cloud edition — Community hard caps (5 workflows / 1 user) would
        // otherwise block multi-workflow / member-add scenarios. Community enforcement is unit-tested.
        Environment.SetEnvironmentVariable("EDITION", "enterprise");

        await _postgres.StartAsync();
        await _redis.StartAsync();
        _redisClient = await ConnectionMultiplexer.ConnectAsync($"{_redis.GetConnectionString()},allowAdmin=true");

        _factory = new WebApplicationFactory<Flowamaz.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<FlowAmazDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<FlowAmazDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));

                services.RemoveAll<IConnectionMultiplexer>();
                services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(_redis.GetConnectionString()));
            });
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _redisClient.DisposeAsync();
        await _factory.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    public async Task ResetRedisAsync()
    {
        var endpoint = _redisClient.GetEndPoints()[0];
        await _redisClient.GetServer(endpoint).FlushDatabaseAsync();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<IntegrationApiFixture>;
