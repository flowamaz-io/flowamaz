using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;

namespace Flowamaz.Tests.Integration.Phase7;

/// <summary>
/// Workflow template gallery over the real server (prompt 07-03/07-07): the seeded official
/// templates are listable anonymously; installing creates a WorkflowDefinition from the template
/// YAML and atomically increments install_count; publishing a published workflow returns a pending
/// community submission.
/// </summary>
[Collection("api")]
public class Phase7TemplateTests : ApiTestBase
{
    public Phase7TemplateTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: tmpl, version: v1, name: Tmpl }
        nodes:
          - { id: start, type: Trigger }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: done }
        """;


    [Fact]
    public async Task List_templates_returns_seeded_official_templates_anonymously()
    {
        var resp = await Fixture.NewClient().GetAsync("/api/v1/templates?pageSize=50");

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var data = await DataAsync(resp);
        // 10 official templates are seeded (WorkflowTemplateSeeder.OfficialTemplates).
        data.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(10);
        var items = data.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(10);
        items.EnumerateArray().Should().Contain(i => i.GetProperty("isOfficial").GetBoolean());
    }

    [Fact]
    public async Task Install_template_creates_workflow_and_increments_install_count()
    {
        var owner = await RegisterOwnerAsync("p7-tmpl-install");
        var ws = await CreateWorkspaceAsync(owner.Client, "Tmpl", "p7-tmpl-i");

        // Pick the first official template.
        var listResp = await Fixture.NewClient().GetAsync("/api/v1/templates?pageSize=50");
        var template = (await DataAsync(listResp)).GetProperty("items").EnumerateArray().First();
        var templateId = template.GetProperty("id").GetGuid();
        var installCountBefore = template.GetProperty("installCount").GetInt32();

        var install = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/templates/{templateId}/install", new { name = "Installed From Template" });

        install.StatusCode.Should().Be(HttpStatusCode.OK, await install.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(install)).GetProperty("workflowId").GetGuid();
        workflowId.Should().NotBeEmpty();

        // The new workflow exists in the workspace.
        var got = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}");
        got.StatusCode.Should().Be(HttpStatusCode.OK);

        // install_count incremented for that template.
        var detail = await Fixture.NewClient().GetAsync($"/api/v1/templates/{templateId}");
        (await DataAsync(detail)).GetProperty("installCount").GetInt32()
            .Should().BeGreaterThan(installCountBefore);
    }

    [Fact]
    public async Task Publish_unpublished_workflow_as_template_is_rejected()
    {
        // Publishing as a community template requires the source workflow to be in Published status
        // first (Designer-gated, workspace-scoped). A draft workflow is refused with an actionable error.
        var owner = await RegisterOwnerAsync("p7-tmpl-publish");
        var ws = await CreateWorkspaceAsync(owner.Client, "Tmpl", "p7-tmpl-p");

        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Draft", slug = "draft-flow", yamlContent = Yaml, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        var publish = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/templates/publish", new
        {
            workflowId,
            name = "My Community Template",
            description = "A reusable workflow for the community gallery.",
            category = "operations",
            tags = new[] { "starter" },
            previewImageUrl = (string?)null,
        });

        // A draft cannot be published as a template — the request is refused (no template is created).
        publish.IsSuccessStatusCode.Should().BeFalse();
    }
}
