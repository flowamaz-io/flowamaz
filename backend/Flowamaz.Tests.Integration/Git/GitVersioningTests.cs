using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flowamaz.Tests.Integration.Common;
using FluentAssertions;

namespace Flowamaz.Tests.Integration.Git;

/// <summary>
/// End-to-end Git versioning (prompt 05-01): create → edit → publish produces a history; the diff
/// reports node-level changes; and historical YAML is reconstructed from a commit SHA, not current.
/// </summary>
[Collection("api")]
public class GitVersioningTests : ApiTestBase
{
    public GitVersioningTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string YamlV1 = """
        workflow: { id: w, version: v1, name: Orders }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action, label: First }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    private const string YamlV2 = """
        workflow: { id: w, version: v1, name: Orders }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action, label: Renamed }
          - { id: b, type: Action, label: Second }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: b }
          - { id: e3, from: b, to: done }
        """;

    [Fact]
    public async Task Create_edit_publish_then_history_diff_and_historical_yaml()
    {
        var owner = await RegisterOwnerAsync("git-ver");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "Versioning WS", "git-ver-ws");

        // Create (commit v1) → edit (commit v2) → publish.
        var createResp = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows",
            new { name = "Orders", slug = "orders", yamlContent = YamlV1, nlDescription = (string?)null, createdByMethod = "NaturalLanguage" });
        createResp.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(createResp)).GetProperty("id").GetGuid();

        var editResp = await owner.Client.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}",
            new { yamlContent = YamlV2 });
        editResp.EnsureSuccessStatusCode();

        var publishResp = await owner.Client.PostAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/publish", null);
        publishResp.EnsureSuccessStatusCode();

        // History: v1 + v2 changed the file (publish re-committed identical content → folded away).
        var historyResp = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/history");
        historyResp.EnsureSuccessStatusCode();
        var history = (await DataAsync(historyResp)).EnumerateArray().ToList();
        history.Should().HaveCount(2);
        history[0].GetProperty("message").GetString().Should().Be("Update Orders");
        history[1].GetProperty("message").GetString().Should().Be("Create Orders");

        var v2Sha = history[0].GetProperty("sha").GetString()!;
        var v1Sha = history[1].GetProperty("sha").GetString()!;

        // Diff v1→v2: node 'b' added, node 'a' modified.
        var diffResp = await owner.Client.GetAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/diff?from={v1Sha}&to={v2Sha}");
        diffResp.EnsureSuccessStatusCode();
        var summary = (await DataAsync(diffResp)).GetProperty("summary");
        summary.GetProperty("addedNodes").EnumerateArray().Select(e => e.GetString()).Should().Contain("b");
        summary.GetProperty("modifiedNodes").EnumerateArray().Select(e => e.GetString()).Should().Contain("a");

        // Historical reconstruction: the v1 commit still yields the ORIGINAL YAML, not the current.
        var atResp = await owner.Client.GetAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/at/{v1Sha}");
        atResp.EnsureSuccessStatusCode();
        (await DataAsync(atResp)).GetProperty("yamlContent").GetString().Should().Be(YamlV1);
    }

    [Fact]
    public async Task Diff_without_both_shas_returns_bad_request()
    {
        var owner = await RegisterOwnerAsync("git-ver-bad");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "git-ver-bad-ws");
        var createResp = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows",
            new { name = "Orders", slug = "orders", yamlContent = YamlV1, nlDescription = (string?)null, createdByMethod = "NaturalLanguage" });
        createResp.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(createResp)).GetProperty("id").GetGuid();

        var resp = await owner.Client.GetAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/diff?from=&to=");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
