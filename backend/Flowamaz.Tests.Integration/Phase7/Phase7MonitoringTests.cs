using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase7;

/// <summary>
/// Advanced monitoring over the real server (prompt 07-04/07-07): replaying a completed instance
/// creates a lineage-tracked TEST instance; replaying a running (non-terminal) instance is rejected
/// with 409; per-node input snapshots are captured on execution with sensitive variables stripped.
/// The background worker is disabled in tests, so runs are driven by invoking the orchestrator
/// directly (the same code the worker runs).
/// </summary>
[Collection("api")]
public class Phase7MonitoringTests : ApiTestBase
{
    public Phase7MonitoringTests(IntegrationApiFixture fixture) : base(fixture) { }

    // Trigger → End completes in a single step.
    private const string TerminalYaml = """
        workflow: { id: mon, version: v1, name: Mon }
        nodes:
          - { id: start, type: Trigger }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: done }
        """;

    // Trigger → Action → End: the Action node surfaces as NodeStarted (with an input snapshot) and
    // then waits for an external executor — which never runs in tests, so the instance stays alive.
    private const string ActionYaml = """
        workflow: { id: mon, version: v1, name: Mon }
        nodes:
          - { id: start, type: Trigger }
          - { id: act, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: act }
          - { id: e2, from: act, to: done }
        """;

    private async Task<(Guid Workspace, Guid WorkflowId)> CreatePublishedAsync(Owner owner, string wsSlug, string yaml)
    {
        var ws = await CreateWorkspaceAsync(owner.Client, "Mon", wsSlug);
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Mon", slug = "mon-flow", yamlContent = yaml, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();
        return (ws, workflowId);
    }

    private async Task<Guid> TriggerAsync(Owner owner, Guid ws, Guid workflowId)
    {
        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        trigger.EnsureSuccessStatusCode();
        return (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();
    }

    private async Task DriveToCompletionAsync(Guid instanceId)
    {
        for (var i = 0; i < 10; i++)
        {
            using var scope = Fixture.Services.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>();
            var result = await orchestrator.StepAsync(instanceId, "test-driver");
            if (result.IsComplete || result.NextNodes.Count == 0) return;
        }
    }

    private async Task StepOnceAsync(Guid instanceId)
    {
        using var scope = Fixture.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>();
        await orchestrator.StepAsync(instanceId, "test-driver");
    }

    [Fact]
    public async Task Replay_completed_instance_creates_test_instance_with_lineage()
    {
        var owner = await RegisterOwnerAsync("p7-mon-replay");
        var (ws, workflowId) = await CreatePublishedAsync(owner, "p7-mon-r", TerminalYaml);
        var instanceId = await TriggerAsync(owner, ws, workflowId);
        await DriveToCompletionAsync(instanceId);

        var replay = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/instances/{instanceId}/replay", new { payloadOverride = (string?)null });

        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var data = await DataAsync(replay);
        data.GetProperty("isTest").GetBoolean().Should().BeTrue();
        var newInstanceId = data.GetProperty("instanceId").GetGuid();
        newInstanceId.Should().NotBe(instanceId);

        // The new instance is a test run with the original as parent.
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var replayInstance = await db.WorkflowInstances.AsNoTracking().FirstAsync(x => x.Id == newInstanceId);
        replayInstance.IsTest.Should().BeTrue();
        replayInstance.ParentInstanceId.Should().Be(instanceId);
    }

    [Fact]
    public async Task Replay_running_instance_returns_409()
    {
        var owner = await RegisterOwnerAsync("p7-mon-running");
        var (ws, workflowId) = await CreatePublishedAsync(owner, "p7-mon-rn", TerminalYaml);
        // Trigger but do NOT drive — the instance is Pending (non-terminal), so replay is rejected.
        var instanceId = await TriggerAsync(owner, ws, workflowId);

        var replay = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/instances/{instanceId}/replay", new { payloadOverride = (string?)null });

        replay.StatusCode.Should().Be(HttpStatusCode.Conflict, await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Node_input_snapshots_are_captured_with_sensitive_variables_stripped()
    {
        var owner = await RegisterOwnerAsync("p7-mon-snap");
        var (ws, workflowId) = await CreatePublishedAsync(owner, "p7-mon-s", ActionYaml);
        var instanceId = await TriggerAsync(owner, ws, workflowId);

        // Seed a non-sensitive and a sensitive variable on the instance before the Action node runs.
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.WorkflowVariables.AddRange(
                new WorkflowVariable { WorkspaceId = ws, InstanceId = instanceId, Name = "fund_name", Value = "\"Tech Growth Fund\"", IsSensitive = false },
                new WorkflowVariable { WorkspaceId = ws, InstanceId = instanceId, Name = "api_secret", Value = "\"shh-do-not-leak\"", IsSensitive = true });
            await db.SaveChangesAsync();
        }

        // Drive past the Trigger so the Action node surfaces a NodeStarted event with an input snapshot.
        await StepOnceAsync(instanceId);
        await StepOnceAsync(instanceId);

        using var verifyScope = Fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var nodeStarted = await verifyDb.WorkflowEvents.AsNoTracking()
            .Where(e => e.InstanceId == instanceId && e.EventType == "NodeStarted" && e.NodeId == "act")
            .FirstOrDefaultAsync();

        nodeStarted.Should().NotBeNull("the Action node records a NodeStarted event with an input snapshot");
        nodeStarted!.InputSnapshot.Should().NotBeNull();
        nodeStarted.InputSnapshot.Should().Contain("fund_name");
        // Sensitive variable name and value must be stripped entirely.
        nodeStarted.InputSnapshot.Should().NotContain("api_secret");
        nodeStarted.InputSnapshot.Should().NotContain("shh-do-not-leak");
    }
}
