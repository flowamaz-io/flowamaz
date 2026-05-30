using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Workflow.Gates;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase4;

/// <summary>
/// Phase 4 human-gate integration tests: decide approve/reject via portal API,
/// and approve/reject via HMAC-signed email links.
/// </summary>
[Collection("api")]
public class Phase4GateTests : ApiTestBase
{
    public Phase4GateTests(IntegrationApiFixture fixture) : base(fixture) { }

    // Deciding a gate resumes the workflow instance via the orchestrator, so the gate must sit on a
    // real published+triggered instance with a pending HumanGate node state (the worker is disabled
    // in the test host, so we trigger and seed the node state directly). Node id is "approval".
    private const string GatedYaml = """
        workflow: { id: p4gated, version: v1, name: P4Gated }
        nodes:
          - { id: start, type: Trigger }
          - { id: approval, type: HumanGate }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: approval }
          - { id: e2, from: approval, to: done }
        """;

    private async Task<(Owner Owner, Guid Ws, Guid InstanceId)> SetupGatedInstanceAsync(string slug)
    {
        var owner = await RegisterOwnerAsync(slug);
        var ws = await CreateWorkspaceAsync(owner.Client, "P4 Gate", slug);

        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Gated", slug = "gated", yamlContent = GatedYaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);

        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();
        return (owner, ws, instanceId);
    }

    /// <summary>Seeds the pending HumanGate node state + GateDecision the decide/email paths act on.</summary>
    private async Task<Guid> SeedGateAsync(
        Guid ws, Guid instanceId, string nodeId, GateDeliveryChannel channel, DateTime expiresAt)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        db.WorkflowNodeStates.Add(new WorkflowNodeState
        {
            WorkspaceId = ws, InstanceId = instanceId, NodeId = nodeId, NodeType = "HumanGate", Status = NodeStatus.Pending,
        });
        var gate = new GateDecision
        {
            Id = Guid.NewGuid(),
            WorkspaceId = ws, InstanceId = instanceId, NodeId = nodeId,
            Decision = GateDecisionStatus.Pending,
            DeliveryChannel = channel,
            DeliveryStatus = GateDeliveryStatus.Sent,
            ExpiresAt = expiresAt,
        };
        db.GateDecisions.Add(gate);
        await db.SaveChangesAsync();
        return gate.Id;
    }

    // ── Decide Approve ────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideGate_Approve_GateStatusApproved()
    {
        var (owner, ws, instanceId) = await SetupGatedInstanceAsync("p4-gate-approve");
        await SeedGateAsync(ws, instanceId, "approval", GateDeliveryChannel.Portal, DateTime.UtcNow.AddHours(24));

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/gates/{instanceId}/approval/decide",
            new { decision = "approved", note = "LGTM" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = await DataAsync(response);
        data.GetProperty("decision").GetString().Should().BeOneOf("Approved", "approved");
    }

    // ── Decide Reject ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideGate_Reject_GateStatusRejected()
    {
        var (owner, ws, instanceId) = await SetupGatedInstanceAsync("p4-gate-reject");
        await SeedGateAsync(ws, instanceId, "approval", GateDeliveryChannel.Portal, DateTime.UtcNow.AddHours(24));

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/gates/{instanceId}/approval/decide",
            new { decision = "rejected", note = "Not approved." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = await DataAsync(response);
        data.GetProperty("decision").GetString().Should().BeOneOf("Rejected", "rejected");
    }

    // ── List pending gates — returns array ────────────────────────────────────

    [Fact]
    public async Task ListPendingGates_ReturnsPendingGates()
    {
        var owner = await RegisterOwnerAsync("p4-gate-list");
        var ws = await CreateWorkspaceAsync(owner.Client, "P4 Gate List", "p4-gate-list");

        var response = await owner.Client.GetAsync(
            $"/api/v1/workspaces/{ws}/gates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        data.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ── Email link approve — valid HMAC ───────────────────────────────────────

    [Fact]
    public async Task EmailLink_ValidHmac_GateApproved()
    {
        // Approving resumes the instance, so seed the gate on a real published+triggered instance.
        var (_, ws, instanceId) = await SetupGatedInstanceAsync("p4-gate-email");
        var expiresAt = DateTime.UtcNow.AddHours(24);
        var gateId = await SeedGateAsync(ws, instanceId, "approval", GateDeliveryChannel.Email, expiresAt);

        // Build the same HMAC the controller builds (key set in IntegrationApiFixture)
        var signingKey = IntegrationApiFixture.GateSigningKey;
        var expiresUnix = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();
        var message = $"{gateId}:approve:{expiresUnix}";
        var sig = GateHmacHelper.BuildHmac(message, signingKey);

        var client = Fixture.NewClient();
        var response = await client.GetAsync($"/api/v1/gates/{gateId}/approve?sig={sig}");

        // Should return HTML (success page)
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Decision Recorded");

        // DB record should be Approved
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var gate = await db.GateDecisions.AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == gateId);
            gate.Should().NotBeNull();
            gate!.Decision.Should().Be(GateDecisionStatus.Approved);
        }
    }

    // ── Email link approve — invalid HMAC returns error HTML ─────────────────

    [Fact]
    public async Task EmailLink_InvalidHmac_ReturnsErrorHtml()
    {
        var gateId = Guid.NewGuid();

        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.GateDecisions.Add(new GateDecision
            {
                Id = gateId,
                WorkspaceId = Guid.NewGuid(),
                InstanceId = Guid.NewGuid(),
                NodeId = "gate-bad-sig",
                Decision = GateDecisionStatus.Pending,
                DeliveryChannel = GateDeliveryChannel.Email,
                DeliveryStatus = GateDeliveryStatus.Sent,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var client = Fixture.NewClient();
        var response = await client.GetAsync($"/api/v1/gates/{gateId}/approve?sig=invalid_sig_abc");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Link Invalid or Expired");
    }
}
