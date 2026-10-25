using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// FUNCTIONAL.md §13.2: the public API is limited per minute by the organisation's plan (Community 10/min),
/// and an organisation's own agreed figure overrides its plan.
/// </summary>
[Collection("api")]
public class PublicApiPlanRateLimitTests : ApiTestBase
{
    public PublicApiPlanRateLimitTests(IntegrationApiFixture fixture) : base(fixture) { }

    private async Task<(Guid OrgId, HttpClient Client)> KeyClientAsync(string slug)
    {
        var owner = await RegisterOwnerAsync(slug); // registers on the Community plan
        var ws = await CreateWorkspaceAsync(owner.Client, "Plan limits", slug);
        Guid envId;
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            envId = (await db.WorkspaceEnvironments.FirstAsync(e => e.WorkspaceId == ws && e.Name == WorkspaceEnvironmentType.Production)).Id;
        }
        var key = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/api-keys",
            new { name = "limits", environmentId = envId, scopes = new[] { "workflows:read" }, expiresAt = (DateTime?)null });
        key.EnsureSuccessStatusCode();
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await DataAsync(key)).GetProperty("plainKey").GetString());
        return (owner.OrgId, client);
    }

    [Fact]
    public async Task Community_plan_allows_10_calls_a_minute_then_429_with_the_plan_named()
    {
        var (_, client) = await KeyClientAsync("plan-community");

        for (var i = 1; i <= 10; i++)
        {
            var ok = await client.GetAsync("/api/public/v1/workflows");
            ok.StatusCode.Should().Be(HttpStatusCode.OK, $"call {i} is within 10/min");
            ok.Headers.GetValues("X-RateLimit-Minute-Limit").Single().Should().Be("10");
            ok.Headers.GetValues("X-RateLimit-Limit").Single().Should().Be("10", "the per-minute limit is the one closest to blocking");
        }

        var blocked = await client.GetAsync("/api/public/v1/workflows");
        blocked.StatusCode.Should().Be((HttpStatusCode)429);
        blocked.Headers.Contains("Retry-After").Should().BeTrue();
        using var doc = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("Community plan allows 10 API calls a minute").And.Contain("upgrade");
    }

    [Fact]
    public async Task An_organisations_own_figure_overrides_its_plan()
    {
        var (orgId, client) = await KeyClientAsync("plan-override");
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            await db.Organisations.Where(o => o.Id == orgId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ApiRateLimitPerMinute, 3));
        }

        for (var i = 0; i < 3; i++)
            (await client.GetAsync("/api/public/v1/workflows")).StatusCode.Should().Be(HttpStatusCode.OK);
        var blocked = await client.GetAsync("/api/public/v1/workflows");
        blocked.StatusCode.Should().Be((HttpStatusCode)429);
        using var doc = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().StartWith("Your organisation's limit is 3 API calls a minute").And.NotContain("Community plan");
    }
}
