using System.Net.Http.Json;
using Flowamaz.Tests.Integration.Common;
using FluentAssertions;

namespace Flowamaz.Tests.Integration.Phase5;

/// <summary>
/// Edition usage end-to-end (prompt 05-08). The integration suite runs as a cloud edition
/// (EDITION=enterprise), so it verifies the /usage endpoint and that cloud editions do NOT hard-block
/// creation beyond a community cap. The Community 6th-workflow → 429 behaviour is unit-tested in
/// EditionServiceTests (community edition can't share this single cloud-configured WebApplicationFactory).
/// </summary>
[Collection("api")]
public class Phase5EditionTests : ApiTestBase
{
    public Phase5EditionTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: w, version: v1, name: W }
        nodes:
          - { id: start, type: Trigger }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: done }
        """;

    [Fact]
    public async Task Usage_endpoint_reports_edition_and_workflow_count()
    {
        var owner = await RegisterOwnerAsync("usage-e2e");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "Usage WS", "usage-e2e-ws");

        await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows",
            new { name = "W1", slug = "w1", yamlContent = Yaml, nlDescription = (string?)null, createdByMethod = "NaturalLanguage" });

        var usageResp = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/usage");
        usageResp.EnsureSuccessStatusCode();
        var data = await DataAsync(usageResp);

        data.GetProperty("edition").GetString().Should().Be("enterprise");
        data.GetProperty("isCommunity").GetBoolean().Should().BeFalse();
        data.GetProperty("workflowsUsed").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Cloud_edition_does_not_block_beyond_community_cap()
    {
        var owner = await RegisterOwnerAsync("nolimit-e2e");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "NoLimit WS", "nolimit-e2e-ws");

        // 6 workflows — would 429 under Community, must all succeed under a cloud edition.
        for (var i = 1; i <= 6; i++)
        {
            var resp = await owner.Client.PostAsJsonAsync(
                $"/api/v1/workspaces/{workspaceId}/workflows",
                new { name = $"W{i}", slug = $"w{i}", yamlContent = Yaml, nlDescription = (string?)null, createdByMethod = "NaturalLanguage" });
            resp.EnsureSuccessStatusCode();
        }
    }
}
