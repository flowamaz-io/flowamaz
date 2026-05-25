using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;

namespace Flowamaz.Tests.Integration.Workspace;

[Collection("api")]
public class WorkspaceApiTests : ApiTestBase
{
    public WorkspaceApiTests(IntegrationApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Create_then_get_returns_workspace_with_default_settings()
    {
        var owner = await RegisterOwnerAsync("ws-create");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "Finance", "finance");

        var get = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await DataAsync(get);
        data.GetProperty("slug").GetString().Should().Be("finance");
        data.GetProperty("settings").GetProperty("allowedAiProviders")[0].GetString().Should().Be("anthropic");
    }

    [Fact]
    public async Task Another_orgs_workspace_returns_404_not_403()
    {
        var ownerA = await RegisterOwnerAsync("ws-orga");
        var workspaceId = await CreateWorkspaceAsync(ownerA.Client, "A WS", "a-ws");

        var ownerB = await RegisterOwnerAsync("ws-orgb");
        var get = await ownerB.Client.GetAsync($"/api/v1/workspaces/{workspaceId}");

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_settings_persists_changes()
    {
        var owner = await RegisterOwnerAsync("ws-settings");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "Ops", "ops");

        var update = await owner.Client.PutAsJsonAsync($"/api/v1/workspaces/{workspaceId}/settings", new
        {
            maxConcurrentRuns = 25,
            runRetentionDays = 30,
            aiCostBudgetMonthUsd = 100.0m,
            allowedAiProviders = new[] { "anthropic" },
            defaultAiModelOverrides = new Dictionary<string, string>(),
            marketplacePolicy = "AllowAll",
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(update)).GetProperty("settings").GetProperty("maxConcurrentRuns").GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task Update_settings_rejects_unknown_provider()
    {
        var owner = await RegisterOwnerAsync("ws-badprov");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "X", "x-ws");

        var update = await owner.Client.PutAsJsonAsync($"/api/v1/workspaces/{workspaceId}/settings", new
        {
            maxConcurrentRuns = 10,
            runRetentionDays = 7,
            aiCostBudgetMonthUsd = 0m,
            allowedAiProviders = new[] { "not-a-provider" },
            defaultAiModelOverrides = new Dictionary<string, string>(),
            marketplacePolicy = "OfficialAndVerified",
        });
        ((int)update.StatusCode).Should().BeGreaterThanOrEqualTo(400).And.BeLessThan(500);
    }

    [Fact]
    public async Task Ai_config_get_returns_functions_and_allowed_providers()
    {
        var owner = await RegisterOwnerAsync("ws-aiget");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "AI", "ai-ws");

        var get = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/ai-config");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await DataAsync(get);
        data.GetProperty("allowedProviders").EnumerateArray().Select(p => p.GetString()).Should().Contain("anthropic");
        data.GetProperty("functions").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Ai_config_rejects_provider_not_in_org_allowlist()
    {
        // Community plan allows Anthropic only — choosing an Azure model must be rejected.
        var owner = await RegisterOwnerAsync("ws-aiprov");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "AI", "ai-prov");

        var update = await owner.Client.PutAsJsonAsync($"/api/v1/workspaces/{workspaceId}/ai-config", new
        {
            overrides = new[]
            {
                new { functionId = "copilot", provider = "azure-openai", modelId = "gpt-4o", keySource = "Byok" },
            },
        });
        ((int)update.StatusCode).Should().BeGreaterThanOrEqualTo(400).And.BeLessThan(500);
    }
}
