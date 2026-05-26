using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Workflow;

/// <summary>
/// Human-approval gate API (prompt 02-04): pending gates are listed, an approval records the
/// decision and resumes the instance, a rejection records the decision and fails the gate node.
/// </summary>
[Collection("api")]
public class GateApiTests : ApiTestBase
{
    public GateApiTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: gated, version: v1, name: Gated }
        nodes:
          - { id: start, type: Trigger }
          - { id: approval, type: HumanGate }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: approval }
          - { id: e2, from: approval, to: done }
        """;

    [Fact]
    public async Task Approve_records_decision_and_returns_approved()
    {
        var (owner, ws, instanceId) = await SetupGatedInstanceAsync("gate-ok");
        await SeedGateAsync(ws, instanceId, "approval");

        var decide = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/gates/{instanceId}/approval/decide", new { decision = "approved", note = "looks good" });

        decide.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(decide)).GetProperty("decision").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task Reject_records_decision_and_returns_rejected()
    {
        var (owner, ws, instanceId) = await SetupGatedInstanceAsync("gate-no");
        await SeedGateAsync(ws, instanceId, "approval");

        var decide = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/gates/{instanceId}/approval/decide", new { decision = "rejected", note = "denied" });

        decide.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(decide)).GetProperty("decision").GetString().Should().Be("Rejected");
    }

    [Fact]
    public async Task List_pending_includes_open_gate()
    {
        var (owner, ws, instanceId) = await SetupGatedInstanceAsync("gate-list");
        await SeedGateAsync(ws, instanceId, "approval");

        var list = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/gates");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(list);
        data.EnumerateArray().Should().Contain(g => g.GetProperty("nodeId").GetString() == "approval");
    }

    private async Task<(Owner Owner, Guid Ws, Guid InstanceId)> SetupGatedInstanceAsync(string slug)
    {
        var owner = await RegisterOwnerAsync(slug);
        var ws = await CreateWorkspaceAsync(owner.Client, "Eng", "eng");

        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Gated", slug = "gated", yamlContent = Yaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);

        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();
        return (owner, ws, instanceId);
    }

    private async Task SeedGateAsync(Guid ws, Guid instanceId, string nodeId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        db.WorkflowNodeStates.Add(new WorkflowNodeState
        {
            WorkspaceId = ws, InstanceId = instanceId, NodeId = nodeId, NodeType = "HumanGate", Status = NodeStatus.Pending,
        });
        db.GateDecisions.Add(new GateDecision
        {
            WorkspaceId = ws, InstanceId = instanceId, NodeId = nodeId, Decision = GateDecisionStatus.Pending,
            DeliveryChannel = GateDeliveryChannel.Portal, DeliveryStatus = GateDeliveryStatus.Sent,
        });
        await db.SaveChangesAsync();
    }
}
