using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
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

    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        // Disable the background worker + Quartz jobs for tests — the live polling loop would race
        // API writes (sequence numbers / node states). The full execution loop is exercised in 02-08.
        Environment.SetEnvironmentVariable("Worker__Enabled", "false");
        // A dummy platform key so F5 model resolution succeeds for the interpreter. The real
        // AiCompletionService would now make a live Anthropic call with this key, so the completion
        // service is replaced with a local stub below (integration tests never hit external AI).
        Environment.SetEnvironmentVariable("Ai__AnthropicPlatformKey", "test-platform-key");

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

                // Integration tests never call external AI — stub the completion (the dummy platform
                // key would otherwise drive a live Anthropic request from AiCompletionService).
                services.RemoveAll<IAiCompletionService>();
                services.AddSingleton<IAiCompletionService, StubAiCompletionService>();
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

/// <summary>
/// Deterministic local completion for integration tests — echoes the prompt (so instance values flow
/// into narratives) without making a network call. Mirrors the dev fallback in AiCompletionService.
/// </summary>
internal sealed class StubAiCompletionService : IAiCompletionService
{
    public Task<AiCompletionResult> CompleteAsync(
        ModelConfig config, string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var text = $"Status summary:\n{userPrompt.Trim()}";
        return Task.FromResult(new AiCompletionResult(text, Math.Max(1, userPrompt.Length / 4), Math.Max(1, text.Length / 4)));
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<IntegrationApiFixture>;
