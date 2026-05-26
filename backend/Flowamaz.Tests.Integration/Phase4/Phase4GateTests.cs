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

    // Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a pending GateDecision directly into the DB and returns its id.
    /// The instance/node IDs are arbitrary — GateService just needs the row to exist.
    /// </summary>
    private async Task<(Guid gateId, Guid instanceId, string nodeId, Guid workspaceId)> SeedPendingGateAsync()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();

        var workspaceId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var nodeId = $"gate-node-{Guid.NewGuid():N}";
        var expiresAt = DateTime.UtcNow.AddHours(24);

        var gate = new GateDecision
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            InstanceId = instanceId,
            NodeId = nodeId,
            Decision = GateDecisionStatus.Pending,
            DeliveryChannel = GateDeliveryChannel.Portal,
            DeliveryStatus = GateDeliveryStatus.Pending,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.GateDecisions.Add(gate);
        await db.SaveChangesAsync();
        return (gate.Id, instanceId, nodeId, workspaceId);
    }

    // ── Decide Approve ────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideGate_Approve_GateStatusApproved()
    {
        var owner = await RegisterOwnerAsync("p4-gate-approve");
        var (_, instanceId, nodeId, workspaceId) = await SeedPendingGateAsync();

        // Register a workspace for this owner so RBAC passes, and create a matching
        // WorkspaceMember with Operator role so the endpoint is accessible.
        // Alternatively, seed using the owner's real workspaceId from registration.
        var ws = await CreateWorkspaceAsync(owner.Client, "P4 Gate", "p4-gate-approve");

        // Use owner's real workspace — seed a gate in that workspace
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var gateInWs = new GateDecision
            {
                Id = Guid.NewGuid(),
                WorkspaceId = ws,
                InstanceId = instanceId,
                NodeId = nodeId,
                Decision = GateDecisionStatus.Pending,
                DeliveryChannel = GateDeliveryChannel.Portal,
                DeliveryStatus = GateDeliveryStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.GateDecisions.Add(gateInWs);
            await db.SaveChangesAsync();
        }

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/gates/{instanceId}/{nodeId}/decide",
            new { decision = "approved", note = "LGTM" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        data.GetProperty("decision").GetString().Should().BeOneOf("Approved", "approved");
    }

    // ── Decide Reject ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideGate_Reject_GateStatusRejected()
    {
        var owner = await RegisterOwnerAsync("p4-gate-reject");
        var ws = await CreateWorkspaceAsync(owner.Client, "P4 Gate Reject", "p4-gate-reject");
        var instanceId = Guid.NewGuid();
        var nodeId = $"gate-{Guid.NewGuid():N}";

        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.GateDecisions.Add(new GateDecision
            {
                Id = Guid.NewGuid(),
                WorkspaceId = ws,
                InstanceId = instanceId,
                NodeId = nodeId,
                Decision = GateDecisionStatus.Pending,
                DeliveryChannel = GateDeliveryChannel.Portal,
                DeliveryStatus = GateDeliveryStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/gates/{instanceId}/{nodeId}/decide",
            new { decision = "rejected", note = "Not approved." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
        // Seed gate directly in DB
        var gateId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var nodeId = $"gate-email-{Guid.NewGuid():N}";
        var expiresAt = DateTime.UtcNow.AddHours(24);

        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.GateDecisions.Add(new GateDecision
            {
                Id = gateId,
                WorkspaceId = workspaceId,
                InstanceId = instanceId,
                NodeId = nodeId,
                Decision = GateDecisionStatus.Pending,
                DeliveryChannel = GateDeliveryChannel.Email,
                DeliveryStatus = GateDeliveryStatus.Sent,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

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
