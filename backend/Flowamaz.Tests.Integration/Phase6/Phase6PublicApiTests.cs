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
/// Public API over the real server: API-key auth, snake_case envelope, rate-limit headers,
/// workspace isolation, and the unauthorized SDK hint.
/// </summary>
[Collection("api")]
public class Phase6PublicApiTests : ApiTestBase
{
    public Phase6PublicApiTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: pub, version: v1, name: Pub }
        nodes:
          - { id: start, type: Trigger }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: done }
        """;

    private async Task<Guid> ProductionEnvironmentIdAsync(Guid workspaceId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var env = await db.WorkspaceEnvironments
            .FirstAsync(e => e.WorkspaceId == workspaceId && e.Name == WorkspaceEnvironmentType.Production);
        return env.Id;
    }

    private async Task<string> ApiKeyAsync(Owner owner, Guid workspaceId)
    {
        var envId = await ProductionEnvironmentIdAsync(workspaceId);
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/api-keys",
            new { name = "public", environmentId = envId, scopes = new[] { "workflows:read", "workflows:trigger" }, expiresAt = (DateTime?)null });
        create.EnsureSuccessStatusCode();
        return (await DataAsync(create)).GetProperty("plainKey").GetString()!;
    }

    private async Task<(Guid Workspace, string WorkflowSlug)> PublishWorkflowAsync(Owner owner, string wsSlug, string wfSlug)
    {
        var ws = await CreateWorkspaceAsync(owner.Client, "Pub", wsSlug);
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Pub", slug = wfSlug, yamlContent = Yaml, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();
        return (ws, wfSlug);
    }

    private HttpClient KeyClient(string apiKey)
    {
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    [Fact]
    public async Task List_with_api_key_returns_snake_case_published_workflows()
    {
        var owner = await RegisterOwnerAsync("p6-pub1");
        var (ws, slug) = await PublishWorkflowAsync(owner, "pub1", "pub-flow");
        var key = await ApiKeyAsync(owner, ws);

        var resp = await KeyClient(key).GetAsync("/api/public/v1/workflows");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.Should().ContainKey("X-RateLimit-Limit");
        resp.Headers.Should().ContainKey("X-RateLimit-Reset");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain(slug);
        body.Should().Contain("\"data\"");
        body.Should().Contain("\"per_page\""); // snake_case meta
    }

    [Fact]
    public async Task Trigger_by_slug_returns_202()
    {
        var owner = await RegisterOwnerAsync("p6-pub2");
        var (ws, slug) = await PublishWorkflowAsync(owner, "pub2", "trigger-flow");
        var key = await ApiKeyAsync(owner, ws);

        var resp = await KeyClient(key).PostAsync($"/api/public/v1/workflows/{slug}/trigger", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_api_key_returns_401_with_sdk_hint()
    {
        var resp = await Fixture.NewClient().GetAsync("/api/public/v1/workflows");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("unauthorized");
        error.GetProperty("message").GetString().Should().Contain("settings/api-keys");
    }

    [Fact]
    public async Task Api_key_cannot_read_another_workspaces_workflows()
    {
        var owner = await RegisterOwnerAsync("p6-pub3");
        var (_, slugA) = await PublishWorkflowAsync(owner, "pub3a", "secret-flow");
        var (wsB, _) = await PublishWorkflowAsync(owner, "pub3b", "other-flow");
        var keyB = await ApiKeyAsync(owner, wsB);

        var resp = await KeyClient(keyB).GetAsync("/api/public/v1/workflows");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().NotContain(slugA);
    }
}
