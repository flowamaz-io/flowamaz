using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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

namespace Flowamaz.Tests.Integration.Auth;

/// <summary>
/// Boots the real API against throwaway Postgres + Redis containers, applies all migrations, and
/// exercises the auth endpoints end to end. Shared across the test class; each test resets Redis
/// (rate-limit counters) and uses a fresh cookie-aware client.
/// </summary>
public sealed class AuthApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine").WithDatabase("flowamaz_auth_test")
        .WithUsername("flowamaz").WithPassword("flowamaz_test_pw").Build();

    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();

    private WebApplicationFactory<Flowamaz.Api.Program> _factory = null!;
    private ConnectionMultiplexer _redisClient = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        _redisClient = await ConnectionMultiplexer.ConnectAsync($"{_redis.GetConnectionString()},allowAdmin=true");

        _factory = new WebApplicationFactory<Flowamaz.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); // Secure=false cookies + a configured Jwt:Secret
            // Swap persistence/cache registrations to the throwaway containers. Done in
            // ConfigureTestServices (runs after the app's own registration) because in-memory
            // configuration does not reliably override appsettings under WebApplicationFactory.
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

    public HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>Clears Redis rate-limit counters so each test starts with a clean window.</summary>
    public async Task ResetRedisAsync()
    {
        var endpoint = _redisClient.GetEndPoints()[0];
        await _redisClient.GetServer(endpoint).FlushDatabaseAsync();
    }
}

public class AuthEndpointTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;

    public AuthEndpointTests(AuthApiFixture fixture) => _fixture = fixture;

    private static object RegisterBody(string slug, string email = "owner@acme.test", string password = "Sup3rSecret!") => new
    {
        orgName = "Acme Corp",
        orgSlug = slug,
        billingEmail = "billing@acme.test",
        email,
        name = "Ada Owner",
        password,
        planSlug = "starter",
    };

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return root.GetProperty("data");
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return root.GetProperty("message").GetString() ?? "";
    }

    [Fact]
    public async Task Register_then_me_returns_the_authenticated_user()
    {
        await _fixture.ResetRedisAsync();
        var client = _fixture.NewClient();

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody("acme-me", "me@acme.test"));
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await DataAsync(register);
        var accessToken = data.GetProperty("accessToken").GetString();
        accessToken.Should().NotBeNullOrEmpty();
        register.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith("fmz_refresh="));

        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var me = await client.SendAsync(meRequest);

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(me)).GetProperty("email").GetString().Should().Be("me@acme.test");
    }

    [Fact]
    public async Task Me_without_a_token_is_401()
    {
        await _fixture.ResetRedisAsync();
        var client = _fixture.NewClient();

        var me = await client.GetAsync("/api/v1/auth/me");

        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Full_flow_register_login_refresh_logout_then_refresh_fails()
    {
        await _fixture.ResetRedisAsync();
        var client = _fixture.NewClient();

        (await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody("acme-flow", "flow@acme.test")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "flow@acme.test", password = "Sup3rSecret!", orgSlug = "acme-flow" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Cookie from login lets us refresh without a body.
        var refresh = await client.PostAsync("/api/v1/auth/refresh", null);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(refresh)).GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();

        (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // After logout the cookie is cleared, so a further refresh is unauthorized.
        var afterLogout = await client.PostAsync("/api/v1/auth/refresh", null);
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refreshed_token_revokes_the_previous_one()
    {
        await _fixture.ResetRedisAsync();
        var client = _fixture.NewClient();
        await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody("acme-rotate", "rotate@acme.test"));

        // Capture the first refresh cookie value, rotate once, then replay the old cookie.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "rotate@acme.test", password = "Sup3rSecret!", orgSlug = "acme-rotate" });
        var oldCookie = ExtractRefreshCookie(login);

        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Replay the pre-rotation cookie on a fresh client → must be rejected.
        var replayClient = _fixture.NewClient();
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        replay.Headers.Add("Cookie", $"fmz_refresh={oldCookie}");
        (await replayClient.SendAsync(replay)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_and_wrong_email_return_identical_401_message()
    {
        await _fixture.ResetRedisAsync();
        var client = _fixture.NewClient();
        await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody("acme-enum", "real@acme.test"));

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "real@acme.test", password = "WrongPass1", orgSlug = "acme-enum" });
        var wrongEmail = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "ghost@acme.test", password = "WrongPass1", orgSlug = "acme-enum" });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await MessageAsync(wrongPassword)).Should().Be(await MessageAsync(wrongEmail));
        (await MessageAsync(wrongPassword)).Should().Be("Invalid credentials");
    }

    [Fact]
    public async Task Five_failed_logins_lock_the_account()
    {
        await _fixture.ResetRedisAsync();
        var client = _fixture.NewClient();
        await client.PostAsJsonAsync("/api/v1/auth/register", RegisterBody("acme-lock", "lock@acme.test"));

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var bad = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "lock@acme.test", password = "WrongPass1", orgSlug = "acme-lock" });
            bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 6th attempt — even with the correct password — is rejected as locked.
        var locked = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "lock@acme.test", password = "Sup3rSecret!", orgSlug = "acme-lock" });
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await MessageAsync(locked)).Should().Contain("locked");
    }

    private static string ExtractRefreshCookie(HttpResponseMessage response)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("fmz_refresh="));
        var firstSegment = setCookie.Split(';')[0]; // fmz_refresh=<value>
        return firstSegment["fmz_refresh=".Length..];
    }
}
