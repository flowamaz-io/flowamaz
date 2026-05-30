using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;

namespace Flowamaz.Tests.Integration.Phase8;

/// <summary>
/// Multi-workspace lifecycle over the real API + Postgres + Redis (prompt 08-04/08-07): archiving a
/// workspace disables new triggers, leaving as the last admin is refused (409), and creating a second
/// workspace surfaces both in the overview.
/// </summary>
[Collection("api")]
public class Phase8WorkspaceTests : ApiTestBase
{
    public Phase8WorkspaceTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: ws, version: v1, name: Ws }
        nodes:
          - { id: start, type: Trigger }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: done }
        """;

    [Fact]
    public async Task Archive_workspace_sets_status_and_disables_triggers()
    {
        var owner = await RegisterOwnerAsync("p8-archive");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "p8-archive-ws");

        // Publish a workflow so it is triggerable.
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Flow", slug = "flow", yamlContent = Yaml, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();

        // Archive the workspace.
        var archive = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/archive", null);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent, await archive.Content.ReadAsStringAsync());

        // The workspace now reports Archived.
        var detail = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}");
        (await DataAsync(detail)).GetProperty("status").GetString().Should().Be("Archived");

        // New triggers are refused with 409.
        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        trigger.StatusCode.Should().Be(HttpStatusCode.Conflict, await trigger.Content.ReadAsStringAsync());

        // Restore re-enables the workspace.
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/restore", null)).EnsureSuccessStatusCode();
        var restored = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}");
        (await DataAsync(restored)).GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task Leave_workspace_as_last_admin_is_conflict()
    {
        var owner = await RegisterOwnerAsync("p8-leave");
        var ws = await CreateWorkspaceAsync(owner.Client, "Solo", "p8-leave-ws");

        // The creator is the only (and last) Admin — leaving is refused.
        var leave = await owner.Client.DeleteAsync($"/api/v1/workspaces/{ws}/members/me");

        leave.StatusCode.Should().Be(HttpStatusCode.Conflict, await leave.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_second_workspace_appears_in_overview()
    {
        var owner = await RegisterOwnerAsync("p8-overview");
        var wsA = await CreateWorkspaceAsync(owner.Client, "Alpha", "p8-overview-a");
        var wsB = await CreateWorkspaceAsync(owner.Client, "Beta", "p8-overview-b");

        var overview = await owner.Client.GetAsync("/api/v1/workspaces/overview");
        overview.StatusCode.Should().Be(HttpStatusCode.OK, await overview.Content.ReadAsStringAsync());

        var ids = (await DataAsync(overview)).EnumerateArray().Select(w => w.GetProperty("id").GetGuid()).ToList();
        ids.Should().Contain([wsA, wsB]);
        // Each card carries the user's role + counts + status.
        var alpha = (await DataAsync(await owner.Client.GetAsync("/api/v1/workspaces/overview")))
            .EnumerateArray().First(w => w.GetProperty("id").GetGuid() == wsA);
        alpha.GetProperty("userRole").GetString().Should().Be("Admin");
        alpha.GetProperty("status").GetString().Should().Be("Active");
    }
}
