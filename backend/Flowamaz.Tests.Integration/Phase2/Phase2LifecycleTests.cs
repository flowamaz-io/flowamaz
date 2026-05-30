using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase2;

/// <summary>
/// Full Phase 2 workflow lifecycle over the real API + Postgres + Redis: create → validate YAML →
/// publish → trigger (idempotent) → drive to Completed → timeline → CEO/Auditor narrative → cancel.
/// The background worker is disabled in the test host, so the run is driven deterministically by
/// invoking the orchestrator directly (same code the worker runs).
/// </summary>
[Collection("api")]
public class Phase2LifecycleTests : ApiTestBase
{
    public Phase2LifecycleTests(IntegrationApiFixture fixture) : base(fixture) { }

    // Trigger → End: completes in a single orchestrator step (no external calls).
    private const string Yaml = """
        workflow: { id: life, version: v1, name: Lifecycle }
        nodes:
          - { id: start, type: Trigger }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: done }
        """;

    [Fact]
    public async Task Full_lifecycle_create_publish_trigger_complete_timeline_narrative_cancel()
    {
        var owner = await RegisterOwnerAsync("p2-life");
        var ws = await CreateWorkspaceAsync(owner.Client, "Eng", "eng");

        // 1-2. Create (YAML validated by SfgParser on the way in).
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Lifecycle", slug = "lifecycle", yamlContent = Yaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        // 3. Publish.
        var publish = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4-5. Trigger twice with the same idempotency key → same instance.
        var body = new { workflowDefinitionId = workflowId, idempotencyKey = "life-key" };
        var first = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", body);
        var second = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", body);
        var instanceId = (await DataAsync(first)).GetProperty("instanceId").GetGuid();
        (await DataAsync(second)).GetProperty("instanceId").GetGuid().Should().Be(instanceId);

        // 6. Drive to completion via the orchestrator (worker is disabled in tests).
        await DriveToCompletionAsync(instanceId);

        var detail = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}");
        (await DataAsync(detail)).GetProperty("status").GetString().Should().Be("Completed");

        // 7. Timeline has nodes.
        var timeline = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}/timeline");
        (await DataAsync(timeline)).GetProperty("nodes").GetArrayLength().Should().BeGreaterThan(0);

        // 8. CEO narrative (only available once Completed) carries the run status.
        var ceo = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}/narrative?audience=ceo");
        ceo.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(ceo)).GetProperty("content").GetString().Should().Contain("Completed");

        // 9. Auditor narrative lists the event log.
        var auditor = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}/narrative?audience=auditor");
        (await DataAsync(auditor)).GetProperty("content").GetString().Should().Contain("InstanceStarted");

        // 10. Cancel a fresh (Pending) instance.
        var trigger2 = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", new { workflowDefinitionId = workflowId });
        var instance2 = (await DataAsync(trigger2)).GetProperty("instanceId").GetGuid();
        var cancel = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/instances/{instance2}/cancel", null);
        (await DataAsync(cancel)).GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Create_persists_yaml_without_validating_validation_is_a_separate_step()
    {
        var owner = await RegisterOwnerAsync("p2-badyaml");
        var ws = await CreateWorkspaceAsync(owner.Client, "Eng", "eng");

        // Create no longer parses/validates the SFG on the way in — a draft can be saved with
        // incomplete YAML and validated later via POST /workflows/validate before publish.
        const string noTrigger = """
            nodes:
              - { id: a, type: Action }
              - { id: done, type: End }
            edges:
              - { id: e1, from: a, to: done }
            """;
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Bad", slug = "bad", yamlContent = noTrigger, createdByMethod = "NaturalLanguage" });

        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
    }

    private async Task DriveToCompletionAsync(Guid instanceId)
    {
        for (var i = 0; i < 10; i++)
        {
            using var scope = Fixture.Services.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>();
            var result = await orchestrator.StepAsync(instanceId, "test-driver");
            if (result.IsComplete) return;
            if (result.NextNodes.Count == 0) return;
        }
    }
}
