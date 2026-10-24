using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Workers;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Integration.Engine;

/// <summary>
/// Regression coverage for the Chrome E2E run findings, through the real API and worker loop:
/// B5 (Test tab lists only test runs), B6 (cancelling at a gate closes the node and stops the clock),
/// O3 (timeline in execution order), B11 (dashboard/ROI counts are live and production-only),
/// O2 (test-run gates stay out of the default approval queue) and B9 (Co-pilot timeout pattern).
/// </summary>
[Collection("api")]
public class E2eRunFindingsTests : ApiTestBase
{
    public E2eRunFindingsTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string GateYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: purchase-findings, name: "Purchase findings" }
        spec:
          trigger: { type: manual }
          nodes:
            - { id: purchase-requested, type: trigger, label: Purchase requested }
            - id: manager-approval
              type: human-gate
              label: Manager approval
              config: { delivery_channel: Portal }
            - { id: done, type: end, label: Done }
          edges:
            - { id: e1, from: purchase-requested, to: manager-approval }
            - { id: e2, from: manager-approval, to: done }
        """;

    private const string StraightYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: straight-findings, name: "Straight findings" }
        spec:
          trigger: { type: manual }
          nodes:
            - { id: start, type: trigger, label: Start }
            - { id: done, type: end, label: Done }
          edges:
            - { id: e1, from: start, to: done }
        """;

    private async Task<string> DriveAsync(Guid ws, Guid instanceId, int maxCycles = 15)
    {
        var worker = new OrchestratorWorker(
            Fixture.Services.GetRequiredService<ITaskQueue>(),
            Fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            Fixture.Services.GetRequiredService<IConfiguration>(),
            NullLogger<OrchestratorWorker>.Instance);

        var status = "Pending";
        for (var i = 0; i < maxCycles; i++)
        {
            using (var scope = Fixture.Services.CreateScope())
                await worker.ProcessInstanceAsync(scope.ServiceProvider, ws, instanceId, CancellationToken.None);

            using var read = Fixture.Services.CreateScope();
            var db = read.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            status = (await db.WorkflowInstances.AsNoTracking().FirstAsync(x => x.Id == instanceId)).Status.ToString();
            if (status is "Completed" or "Failed" or "Waiting" or "Cancelled") return status;
        }
        return status;
    }

    private static async Task<Guid> PublishAsync(Owner owner, Guid ws, string slug, string yaml)
    {
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = slug, slug, yamlContent = yaml, createdByMethod = "Canvas" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        var publish = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        return workflowId;
    }

    private static async Task<Guid> TriggerAsync(Owner owner, Guid ws, Guid workflowId, bool isTest = false)
    {
        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId, payload = "{}", isTest });
        trigger.StatusCode.Should().Be(HttpStatusCode.OK, await trigger.Content.ReadAsStringAsync());
        return (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();
    }

    /// <summary>Items of a list response, whether the envelope's data is the array or a paged object.</summary>
    private static async Task<List<JsonElement>> ItemsAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = await DataAsync(response);
        var array = data.ValueKind == JsonValueKind.Array ? data : data.GetProperty("data");
        return array.EnumerateArray().ToList();
    }

    [Fact]
    public async Task B6_O3_cancelling_at_a_gate_closes_the_node_stamps_the_end_and_keeps_execution_order()
    {
        var owner = await RegisterOwnerAsync("e2e-find-cancel");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-find-cancel");
        var workflowId = await PublishAsync(owner, ws, "purchase-findings-cancel", GateYaml);
        var instanceId = await TriggerAsync(owner, ws, workflowId);
        (await DriveAsync(ws, instanceId)).Should().Be("Waiting");

        var cancel = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());

        var detail = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}"));
        detail.GetProperty("status").GetString().Should().Be("Cancelled");
        detail.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.String, "duration must stop at cancellation");
        var nodes = detail.GetProperty("nodeStates").EnumerateArray().ToList();
        var gate = nodes.Single(n => n.GetProperty("nodeId").GetString() == "manager-approval");
        gate.GetProperty("status").GetString().Should().Be("Cancelled");
        nodes.Should().NotContain(n => n.GetProperty("status").GetString() == "Running");
        nodes.Select(n => n.GetProperty("nodeId").GetString()).Should().ContainInOrder("purchase-requested", "manager-approval");

        var timeline = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}/timeline"));
        var timelineNodes = timeline.GetProperty("nodes").EnumerateArray().ToList();
        timelineNodes.Select(n => n.GetProperty("nodeId").GetString()).Should().Equal("purchase-requested", "manager-approval");
        timelineNodes[1].GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.String);
        timeline.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task B5_O2_B11_test_runs_are_separated_from_production_everywhere()
    {
        var owner = await RegisterOwnerAsync("e2e-find-test");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-find-test");
        var gateWorkflow = await PublishAsync(owner, ws, "purchase-findings-test", GateYaml);
        var straight = await PublishAsync(owner, ws, "straight-findings", StraightYaml);

        var prodGateRun = await TriggerAsync(owner, ws, gateWorkflow);
        var testGateRun = await TriggerAsync(owner, ws, gateWorkflow, isTest: true);
        var prodDone1 = await TriggerAsync(owner, ws, straight);
        var prodDone2 = await TriggerAsync(owner, ws, straight);
        (await DriveAsync(ws, prodGateRun)).Should().Be("Waiting");
        (await DriveAsync(ws, testGateRun)).Should().Be("Waiting");
        (await DriveAsync(ws, prodDone1)).Should().Be("Completed");
        (await DriveAsync(ws, prodDone2)).Should().Be("Completed");

        // B5: the Test tab (testOnly) lists only the test run; the Production tab (default) only production.
        var testTab = await ItemsAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances?testOnly=true"));
        testTab.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(testGateRun);
        testTab.Should().OnlyContain(i => i.GetProperty("isTest").GetBoolean());
        var prodTab = await ItemsAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances"));
        prodTab.Select(i => i.GetProperty("id").GetGuid()).Should().BeEquivalentTo([prodGateRun, prodDone1, prodDone2]);

        // O2: the approval queue shows production gates only, unless includeTest=true (then flagged).
        var queue = await ItemsAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/gates"));
        queue.Select(g => g.GetProperty("instanceId").GetGuid()).Should().Equal(prodGateRun);
        var withTest = await ItemsAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/gates?includeTest=true"));
        withTest.Should().HaveCount(2);
        withTest.Single(g => g.GetProperty("instanceId").GetGuid() == testGateRun).GetProperty("isTest").GetBoolean().Should().BeTrue();
        withTest.Single(g => g.GetProperty("instanceId").GetGuid() == prodGateRun).GetProperty("isTest").GetBoolean().Should().BeFalse();

        // B11: counts are live (no hourly metric job ran) and exclude the test run.
        var weather = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/weather"));
        weather.GetProperty("runsThisMonth").GetInt32().Should().Be(3);
        var roi = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/analytics/roi"));
        roi.GetProperty("totalRunsInPeriod").GetInt32().Should().Be(3);
        roi.GetProperty("successfulRuns").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task B9_copilot_timeout_example_is_pattern_matched_without_an_ai_call()
    {
        var owner = await RegisterOwnerAsync("e2e-find-copilot");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-find-copilot");
        var workflowId = await PublishAsync(owner, ws, "purchase-findings-copilot", GateYaml);

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/copilot",
            new { command = "Add a 24h timeout to manager-approval", yamlContent = GateYaml });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = await DataAsync(response);
        data.GetProperty("matchedPattern").GetString().Should().Be("set-gate-timeout");
        data.GetProperty("yamlPatch").GetString().Should().Contain("timeout_hours: 24").And.Contain("id: manager-approval");
        data.GetProperty("cacheHit").GetBoolean().Should().BeFalse();
    }
}
