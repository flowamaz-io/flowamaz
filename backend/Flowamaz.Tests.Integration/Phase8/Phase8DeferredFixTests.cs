using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase8;

/// <summary>
/// End-to-end verification of the fix-07 deferred items (prompt 08-01/08-07): the Critical
/// install→publish→trigger path (SfgParser reads the flowamaz/v1 template YAML), the Stripe checkout
/// redirect-URL allowlist, and the Development breakpoint that pauses an instance before a node.
/// </summary>
[Collection("api")]
public class Phase8DeferredFixTests : ApiTestBase
{
    public Phase8DeferredFixTests(IntegrationApiFixture fixture) : base(fixture) { }

    // Trigger → Action → End: the Action node is where the breakpoint pauses (no executor runs it
    // because the worker is disabled in tests; the orchestrator surfaces it, then the breakpoint hits).
    private const string ActionYaml = """
        workflow: { id: bp, version: v1, name: Bp }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    [Fact]
    public async Task Install_official_template_then_publish_and_trigger_creates_instance()
    {
        var owner = await RegisterOwnerAsync("p8-critical");
        var ws = await CreateWorkspaceAsync(owner.Client, "Critical", "p8-critical-ws");

        // Install the first official template — creates a WorkflowDefinition from its flowamaz/v1 YAML.
        var listResp = await Fixture.NewClient().GetAsync("/api/v1/templates?pageSize=50");
        var templateId = (await DataAsync(listResp)).GetProperty("items").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        var install = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/templates/{templateId}/install", new { name = "From Template" });
        install.StatusCode.Should().Be(HttpStatusCode.OK, await install.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(install)).GetProperty("workflowId").GetGuid();

        // Publish parses the template YAML through SfgParser — the fix-07 Critical fix makes this work.
        var publish = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());

        // Trigger creates an instance from the published version.
        var trigger = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/instances", new { workflowDefinitionId = workflowId });
        trigger.StatusCode.Should().Be(HttpStatusCode.OK, await trigger.Content.ReadAsStringAsync());
        (await DataAsync(trigger)).GetProperty("instanceId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Checkout_with_external_redirect_url_is_rejected()
    {
        var owner = await RegisterOwnerAsync("p8-checkout");

        // success/cancel URLs that point off-platform are rejected by the redirect allowlist validator.
        var checkout = await owner.Client.PostAsJsonAsync("/api/v1/billing/checkout", new
        {
            planId = Guid.NewGuid(),
            annual = false,
            successUrl = "https://evil.example.com/success",
            cancelUrl = "https://evil.example.com/cancel",
        });

        checkout.StatusCode.Should().Be(HttpStatusCode.BadRequest, await checkout.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Breakpoint_pauses_instance_at_node()
    {
        var owner = await RegisterOwnerAsync("p8-breakpoint");
        var ws = await CreateWorkspaceAsync(owner.Client, "Debug", "p8-bp-ws");

        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Bp", slug = "bp", yamlContent = ActionYaml, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();

        // Set a Development breakpoint on the Action node (dev-only endpoint, member-gated).
        var setBp = await owner.Client.PostAsJsonAsync("/api/v1/dev/breakpoints",
            new { workspaceId = ws, workflowId, nodeId = "a" });
        setBp.StatusCode.Should().Be(HttpStatusCode.OK, await setBp.Content.ReadAsStringAsync());

        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();

        // Drive one orchestrator step (the worker is disabled): it should pause at the breakpoint.
        using (var scope = Fixture.Services.CreateScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>();
            await orchestrator.StepAsync(instanceId, "test-driver");
        }

        var detail = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}");
        (await DataAsync(detail)).GetProperty("status").GetString().Should().Be("BreakpointHit");
    }
}
