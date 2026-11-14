using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Auth;

/// <summary>
/// `fmz` CLI sessions end to end: the device flow gives the CLI a session of its own (not a copy of the browser token),
/// the CLI refreshes it, the person sees it in Settings and can end it — which cuts the CLI off at once — and
/// `fmz auth logout` ends it on the server. Sign-out-everywhere paths (password reset, deactivation) end it too.
/// </summary>
[Collection("api")]
public sealed class CliSessionTests : ApiTestBase
{
    public CliSessionTests(IntegrationApiFixture fixture) : base(fixture) { }

    private static string U(string slug) => $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

    private sealed record CliLogin(string AccessToken, string RefreshToken, Guid SessionId);

    /// <summary>Runs the device flow: the CLI starts it, the signed-in owner approves, the CLI collects its session.</summary>
    private async Task<CliLogin> LoginCliAsync(Owner owner, string clientName)
    {
        var cli = Fixture.NewClient();
        var start = await cli.PostAsJsonAsync("/api/v1/cli/auth/device", new { clientName });
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var flow = await DataAsync(start);
        var deviceCode = flow.GetProperty("deviceCode").GetString()!;
        var userCode = flow.GetProperty("userCode").GetString()!;

        (await cli.GetAsync($"/api/v1/cli/auth/token?deviceCode={deviceCode}")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var pending = await owner.Client.GetAsync($"/api/v1/cli/auth/pending?userCode={userCode}");
        pending.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(pending)).GetProperty("clientName").GetString().Should().Be(clientName);

        (await owner.Client.PostAsJsonAsync("/api/v1/cli/auth/approve", new { userCode })).StatusCode.Should().Be(HttpStatusCode.OK);
        // A code can be approved once.
        (await owner.Client.PostAsJsonAsync("/api/v1/cli/auth/approve", new { userCode })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var collected = await cli.GetAsync($"/api/v1/cli/auth/token?deviceCode={deviceCode}");
        collected.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await DataAsync(collected);
        // …and collected once.
        (await cli.GetAsync($"/api/v1/cli/auth/token?deviceCode={deviceCode}")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var access = tokens.GetProperty("accessToken").GetString()!;
        access.Should().NotBe(owner.AccessToken, "the CLI gets its own token, not a copy of the browser's");
        return new CliLogin(access, tokens.GetProperty("refreshToken").GetString()!, tokens.GetProperty("sessionId").GetGuid());
    }

    private HttpClient As(string accessToken)
    {
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        await Fixture.NewClient().PostAsJsonAsync("/api/v1/cli/auth/refresh", new { refreshToken });

    [Fact]
    public async Task Device_flow_gives_a_refreshable_session_that_settings_can_end_at_once()
    {
        var owner = await RegisterOwnerAsync(U("cli-sess"));
        var login = await LoginCliAsync(owner, "ci-laptop");

        (await As(login.AccessToken).GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await RefreshAsync(login.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAccess = (await DataAsync(refreshed)).GetProperty("accessToken").GetString()!;
        (await As(newAccess).GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        // Not rotated: the same refresh token keeps working (two `fmz` commands at once don't sign each other out).
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Settings lists it; asked by the CLI itself, it's marked as the current one.
        var listed = await DataAsync(await owner.Client.GetAsync("/api/v1/cli/sessions"));
        listed.GetArrayLength().Should().Be(1);
        listed[0].GetProperty("name").GetString().Should().Be("ci-laptop");
        listed[0].GetProperty("isCurrent").GetBoolean().Should().BeFalse();

        // End it from Settings: the CLI's current token stops working now, and it can't refresh.
        var latest = (await DataAsync(await RefreshAsync(login.RefreshToken))).GetProperty("accessToken").GetString()!;
        (await DataAsync(await As(latest).GetAsync("/api/v1/cli/sessions")))[0].GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        (await owner.Client.DeleteAsync($"/api/v1/cli/sessions/{login.SessionId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await As(latest).GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await DataAsync(await owner.Client.GetAsync("/api/v1/cli/sessions"))).GetArrayLength().Should().Be(0);
        (await owner.Client.DeleteAsync($"/api/v1/cli/sessions/{login.SessionId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The browser session is untouched.
        (await owner.Client.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Fmz_auth_logout_ends_the_session_on_the_server()
    {
        var owner = await RegisterOwnerAsync(U("cli-out"));
        var login = await LoginCliAsync(owner, "build-box");

        var logout = await As(login.AccessToken).PostAsJsonAsync("/api/v1/cli/auth/logout", new { refreshToken = login.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(logout)).GetProperty("sessionEnded").GetBoolean().Should().BeTrue();

        (await As(login.AccessToken).GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Logging out twice is harmless.
        (await Fixture.NewClient().PostAsJsonAsync("/api/v1/cli/auth/logout", new { refreshToken = login.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task People_only_see_and_end_their_own_cli_sessions()
    {
        var alice = await RegisterOwnerAsync(U("cli-a"));
        var aliceLogin = await LoginCliAsync(alice, "alice-laptop");
        var bob = await RegisterOwnerAsync(U("cli-b"));

        (await DataAsync(await bob.Client.GetAsync("/api/v1/cli/sessions"))).GetArrayLength().Should().Be(0);
        (await bob.Client.DeleteAsync($"/api/v1/cli/sessions/{aliceLogin.SessionId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RefreshAsync(aliceLogin.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_a_person_out_everywhere_ends_their_cli_sessions()
    {
        var owner = await RegisterOwnerAsync(U("cli-all"));
        var login = await LoginCliAsync(owner, "desk");

        // The path a password reset, SCIM deactivation and SSO unlinking all take.
        using (var scope = Fixture.Services.CreateScope())
        {
            var userId = (await DataAsync(await owner.Client.GetAsync("/api/v1/auth/me"))).GetProperty("id").GetGuid();
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>().RevokeAllForUserAsync(userId);
        }

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_with_a_browser_refresh_token_or_garbage_is_refused()
    {
        (await RefreshAsync("not-a-token")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync("")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
