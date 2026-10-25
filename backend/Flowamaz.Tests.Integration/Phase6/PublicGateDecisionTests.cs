using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// Public gate decisions over the real server: <c>decided_by</c> (snake_case body field) is stored on the
/// gate, returned as <c>decided_by_label</c>, and named in the audit log next to the API key; an unknown
/// decision is a 400 in the public error envelope and leaves the gate pending.
/// </summary>
[Collection("api")]
public class PublicGateDecisionTests : ApiTestBase
{
    public PublicGateDecisionTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string GatedYaml = """
        workflow: { id: pubgate, version: v1, name: PubGate }
        nodes:
          - { id: start, type: Trigger }
          - { id: approval, type: HumanGate }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: approval }
          - { id: e2, from: approval, to: done }
        """;

    private async Task<(Guid Ws, Guid GateId, string ApiKey)> SetupAsync(string slug)
    {
        var owner = await RegisterOwnerAsync(slug);
        var ws = await CreateWorkspaceAsync(owner.Client, "Pub Gate", slug);

        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Gated", slug = "gated", yamlContent = GatedYaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();
        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();

        Guid gateId;
        Guid envId;
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            // The worker is disabled in the test host: seed the pending gate the decision acts on.
            db.WorkflowNodeStates.Add(new WorkflowNodeState
            {
                WorkspaceId = ws, InstanceId = instanceId, NodeId = "approval", NodeType = "HumanGate", Status = NodeStatus.Pending,
            });
            var gate = new GateDecision
            {
                Id = Guid.NewGuid(), WorkspaceId = ws, InstanceId = instanceId, NodeId = "approval",
                Decision = GateDecisionStatus.Pending, DeliveryChannel = GateDeliveryChannel.Portal,
                DeliveryStatus = GateDeliveryStatus.Sent, ExpiresAt = DateTime.UtcNow.AddHours(24),
            };
            db.GateDecisions.Add(gate);
            await db.SaveChangesAsync();
            gateId = gate.Id;
            envId = (await db.WorkspaceEnvironments
                .FirstAsync(e => e.WorkspaceId == ws && e.Name == WorkspaceEnvironmentType.Production)).Id;
        }

        var key = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/api-keys",
            new { name = "Loan app", environmentId = envId, scopes = new[] { "instances:read", "instances:write" }, expiresAt = (DateTime?)null });
        key.EnsureSuccessStatusCode();
        return (ws, gateId, (await DataAsync(key)).GetProperty("plainKey").GetString()!);
    }

    private HttpClient KeyClient(string apiKey)
    {
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Decide_with_decided_by_stores_returns_and_audits_the_name()
    {
        var (ws, gateId, key) = await SetupAsync("pgd-named");

        var resp = await KeyClient(key).PostAsync($"/api/public/v1/gates/{gateId}/decide",
            Json("""{"decision":"approved","note":"Within policy","decided_by":"Aisha Rahman (Branch Manager)"}"""));

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, body);
        // Decisions use their own budget (1000/h by default), not the 100/h one for starting runs.
        resp.Headers.GetValues("X-RateLimit-Hour-Limit").Single().Should().Be("1000");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("data").GetProperty("decided_by_label").GetString()
            .Should().Be("Aisha Rahman (Branch Manager)");

        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var gate = await db.GateDecisions.AsNoTracking().IgnoreQueryFilters().SingleAsync(g => g.Id == gateId);
        gate.Decision.Should().Be(GateDecisionStatus.Approved);
        gate.DecidedByLabel.Should().Be("Aisha Rahman (Branch Manager)");
        gate.DecisionNote.Should().Be("Within policy");

        // The audit row is written fire-and-forget; give it a moment.
        string? actorLabel = null;
        for (var i = 0; i < 25 && actorLabel is null; i++)
        {
            await Task.Delay(200);
            actorLabel = await db.AuditEvents.AsNoTracking()
                .Where(e => e.WorkspaceId == ws && e.EventType == "gate.approved")
                .Select(e => e.ActorLabel).FirstOrDefaultAsync();
        }
        actorLabel.Should().StartWith("Aisha Rahman (Branch Manager) via ").And.Contain("Loan app");
    }

    [Fact]
    public async Task Decide_with_unknown_decision_returns_400_and_leaves_the_gate_pending()
    {
        var (_, gateId, key) = await SetupAsync("pgd-typo");

        var resp = await KeyClient(key).PostAsync($"/api/public/v1/gates/{gateId}/decide",
            Json("""{"decision":"aproved","decided_by":"Aisha Rahman"}"""));

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("validation_error");

        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        (await db.GateDecisions.AsNoTracking().IgnoreQueryFilters().SingleAsync(g => g.Id == gateId))
            .Decision.Should().Be(GateDecisionStatus.Pending);
    }

    [Fact]
    public async Task Cancel_through_the_public_api_is_audited_with_the_api_key()
    {
        var (ws, gateId, key) = await SetupAsync("pgd-cancel");
        Guid instanceId;
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            instanceId = (await db.GateDecisions.AsNoTracking().IgnoreQueryFilters().SingleAsync(g => g.Id == gateId)).InstanceId;
        }

        var resp = await KeyClient(key).PostAsync($"/api/public/v1/instances/{instanceId}/cancel", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        using var verify = Fixture.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        Flowamaz.Core.Entities.Audit.AuditEvent? row = null;
        for (var i = 0; i < 25 && row is null; i++)
        {
            await Task.Delay(200);
            row = await verifyDb.AuditEvents.AsNoTracking()
                .FirstOrDefaultAsync(e => e.WorkspaceId == ws && e.EventType == "instance.cancelled");
        }
        row.Should().NotBeNull("every cancel is audited");
        row!.ResourceId.Should().Be(instanceId);
        row.ActorType.Should().Be("api_key");
        row.ActorLabel.Should().Contain("Loan app");
    }
}
