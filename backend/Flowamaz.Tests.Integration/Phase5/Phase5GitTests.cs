using System.Net.Http.Json;
using Flowamaz.Tests.Integration.Common;
using FluentAssertions;

namespace Flowamaz.Tests.Integration.Phase5;

/// <summary>
/// Git versioning end-to-end (prompt 05-08): adding a node between two saves shows up in the diff's
/// added-node summary, and publishing reconstructs the published YAML from its commit. (Broader
/// history/diff coverage lives in <see cref="Git.GitVersioningTests"/>.)
/// </summary>
[Collection("api")]
public class Phase5GitTests : ApiTestBase
{
    public Phase5GitTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string OneActionYaml = """
        workflow: { id: w, version: v1, name: W }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    private const string TwoActionYaml = """
        workflow: { id: w, version: v1, name: W }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action }
          - { id: extra, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: extra }
          - { id: e3, from: extra, to: done }
        """;

    [Fact]
    public async Task Adding_a_node_shows_it_in_diff_added_list()
    {
        var owner = await RegisterOwnerAsync("p5-git");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "p5-git-ws");

        var create = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows",
            new { name = "W", slug = "w", yamlContent = OneActionYaml, nlDescription = (string?)null, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        await owner.Client.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}",
            new { yamlContent = TwoActionYaml });

        var history = (await DataAsync(await owner.Client.GetAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/history"))).EnumerateArray().ToList();
        var toSha = history[0].GetProperty("sha").GetString()!;
        var fromSha = history[1].GetProperty("sha").GetString()!;

        var diff = await DataAsync(await owner.Client.GetAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/diff?from={fromSha}&to={toSha}"));

        diff.GetProperty("summary").GetProperty("addedNodes").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("extra");
    }

    [Fact]
    public async Task Publish_then_history_reconstructs_published_yaml()
    {
        var owner = await RegisterOwnerAsync("p5-git-pub");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "p5-git-pub-ws");

        var create = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows",
            new { name = "W", slug = "w", yamlContent = OneActionYaml, nlDescription = (string?)null, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        (await owner.Client.PostAsync($"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/publish", null))
            .EnsureSuccessStatusCode();

        var history = (await DataAsync(await owner.Client.GetAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/history"))).EnumerateArray().ToList();
        var firstSha = history[^1].GetProperty("sha").GetString()!;

        var at = await DataAsync(await owner.Client.GetAsync(
            $"/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/at/{firstSha}"));
        at.GetProperty("yamlContent").GetString().Should().Be(OneActionYaml);
    }
}
