using System.Net;
using System.Text;
using Flowamaz.Application.Connectors.Services;
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

    /// <summary>
    /// Known Stripe webhook signing secret used in integration tests so a test can compute a valid
    /// Stripe-Signature header (t=&lt;ts&gt;,v1=&lt;HMACSHA256&gt;) and exercise the real
    /// EventUtility.ConstructEvent verification path. Globally safe: it only affects webhook
    /// signature verification, which is never reached by any other test.
    /// </summary>
    public const string StripeWebhookSecret = "whsec_integration_test_secret_phase7";

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
        // Test-only OAuth client credentials so the token-exchange path resolves config instead of
        // throwing "not configured". The actual provider HTTP call is stubbed (see StubOAuthHandler).
        Environment.SetEnvironmentVariable("SLACK_CLIENT_ID", "test-slack-client-id");
        Environment.SetEnvironmentVariable("SLACK_CLIENT_SECRET", "test-slack-client-secret");
        // Master key the credential vault derives per-workspace keys from (OAuth callback stores a token).
        Environment.SetEnvironmentVariable("CREDENTIAL_MASTER_KEY", "integration-test-credential-master-key-32!!");
        // Known Stripe webhook secret so the billing webhook test can compute a valid signature and
        // drive the real EventUtility.ConstructEvent verification. No SecretKey is set, so no Stripe
        // network call is ever made (CreateCheckoutSession degrades to BillingException.NotConfigured).
        Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", StripeWebhookSecret);

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

                // Stub the OAuth token-exchange HttpClient so callback tests never hit the live
                // provider endpoints — returns a deterministic provider-shaped token response.
                services.AddHttpClient(OAuthService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new StubOAuthHandler());
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
/// Primary handler for the "oauth-exchange" HttpClient under test. Returns a provider-shaped
/// success token response (keyed on host) so OAuth callback tests exercise the full exchange →
/// vault-store flow without any outbound network call.
/// </summary>
internal sealed class StubOAuthHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? string.Empty;
        var json = host.Contains("slack", StringComparison.OrdinalIgnoreCase)
            ? """{"ok":true,"access_token":"xoxb-test-token","team":{"id":"T123"},"bot_user_id":"U123"}"""
            : host.Contains("github", StringComparison.OrdinalIgnoreCase)
                ? """{"access_token":"test-github-token","scope":"repo","token_type":"bearer"}"""
                : """{"access_token":"test-ms-token","refresh_token":"test-refresh","expires_in":3600}""";

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<IntegrationApiFixture>;
