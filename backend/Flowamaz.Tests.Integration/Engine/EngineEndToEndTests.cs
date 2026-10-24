using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
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
/// End-to-end runs through the REAL worker loop (<see cref="OrchestratorWorker.ProcessInstanceAsync"/>):
/// step → execute nodes → complete/park. Earlier engine tests only called StepAsync, so node
/// execution, approval gates and connector steps were never exercised together.
/// </summary>
[Collection("api")]
public class EngineEndToEndTests : ApiTestBase
{
    public EngineEndToEndTests(IntegrationApiFixture fixture) : base(fixture) { }

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

    private async Task<Guid> PublishAndTriggerAsync(Owner owner, Guid ws, string slug, string yaml, object payload)
    {
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = slug, slug, yamlContent = yaml, createdByMethod = "Canvas" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        var publish = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());

        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId, payload = System.Text.Json.JsonSerializer.Serialize(payload) });
        trigger.StatusCode.Should().Be(HttpStatusCode.OK, await trigger.Content.ReadAsStringAsync());
        return (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();
    }

    [Fact]
    public async Task ApprovalGate_CreatesApproval_ParksInstance_ThenApprovalCompletesTheRun()
    {
        const string yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata: { id: approval-e2e, name: "Approval E2E" }
            spec:
              trigger: { type: manual }
              nodes:
                - { id: start, type: trigger, label: Start }
                - id: approval
                  type: human-gate
                  label: Manager approval
                  config: { delivery_channel: Portal, assigned_to_variable: approver, gate_label: "Approve PO" }
                - { id: done, type: end, label: Done }
              edges:
                - { id: e1, from: start, to: approval }
                - { id: e2, from: approval, to: done }
            """;
        var owner = await RegisterOwnerAsync("e2e-gate");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-gate");
        var instanceId = await PublishAndTriggerAsync(owner, ws, "approval-e2e", yaml, new { approver = "manager@example.com", amount = 1500 });

        // The run must stop at the gate — not fail (the old behaviour) and not skip past it.
        (await DriveAsync(ws, instanceId)).Should().Be("Waiting");

        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var gates = await db.GateDecisions.AsNoTracking().Where(g => g.InstanceId == instanceId).ToListAsync();
            gates.Should().ContainSingle("re-driving must not open duplicate approvals");
            gates[0].NodeId.Should().Be("approval");
            gates[0].AssignedToEmail.Should().Be("manager@example.com"); // assignee read from trigger payload variables
        }

        var pending = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/gates");
        (await DataAsync(pending)).EnumerateArray().Should().Contain(g => g.GetProperty("nodeId").GetString() == "approval");

        var decide = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/gates/{instanceId}/approval/decide",
            new { decision = "approved", note = "ok" });
        decide.StatusCode.Should().Be(HttpStatusCode.OK, await decide.Content.ReadAsStringAsync());

        (await DriveAsync(ws, instanceId)).Should().Be("Completed");
    }

    // Shape of the official Purchase Approval template: gate → router with label-only branches
    // (via: approved / rejected) and no branch config. Ends are distinct so the path is observable.
    private const string ApprovalRouterYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: approval-router, name: "Approval router" }
        spec:
          trigger: { type: manual }
          nodes:
            - { id: start, type: trigger }
            - id: manager-approval
              type: human-gate
              config: { delivery_channel: Portal }
            - { id: route-decision, type: router, label: "Approved?" }
            - { id: approved-end, type: end }
            - { id: rejected-end, type: end }
          edges:
            - { id: e1, from: start, to: manager-approval }
            - { id: e2, from: manager-approval, to: route-decision }
            - { id: e3, from: route-decision, to: approved-end, via: approved }
            - { id: e4, from: route-decision, to: rejected-end, via: rejected }
        """;

    [Theory]
    [InlineData("approved", "approved-end", "rejected-end")]
    [InlineData("rejected", "rejected-end", "approved-end")]
    public async Task GateThenLabelledRouter_FollowsOnlyTheDecidedBranch(string decision, string taken, string skipped)
    {
        var owner = await RegisterOwnerAsync($"e2e-route-{decision}");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", $"e2e-route-{decision}");
        var instanceId = await PublishAndTriggerAsync(owner, ws, $"approval-router-{decision}", ApprovalRouterYaml, new { });
        (await DriveAsync(ws, instanceId)).Should().Be("Waiting");

        var decide = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/gates/{instanceId}/manager-approval/decide",
            new { decision, note = "e2e" });
        decide.StatusCode.Should().Be(HttpStatusCode.OK, await decide.Content.ReadAsStringAsync());

        (await DriveAsync(ws, instanceId)).Should().Be("Completed", "a rejection with a rejected route continues instead of failing");
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var states = await db.WorkflowNodeStates.AsNoTracking().Where(n => n.InstanceId == instanceId).ToListAsync();
        states.Should().Contain(n => n.NodeId == taken);
        states.Should().NotContain(n => n.NodeId == skipped, "a label-only router must not take every branch");
    }

    [Fact]
    public async Task CancelledRun_ApprovalLeavesTheInbox_AndCannotBeDecided()
    {
        var owner = await RegisterOwnerAsync("e2e-cancel-gate");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-cancel-gate");
        var instanceId = await PublishAndTriggerAsync(owner, ws, "approval-router-cancel", ApprovalRouterYaml, new { });
        (await DriveAsync(ws, instanceId)).Should().Be("Waiting");

        var cancel = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());

        var pending = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/gates");
        (await DataAsync(pending)).EnumerateArray().Should().NotContain(g => g.GetProperty("instanceId").GetGuid() == instanceId);

        var decide = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/gates/{instanceId}/manager-approval/decide",
            new { decision = "approved", note = "too late" });
        decide.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await decide.Content.ReadAsStringAsync()).Should().Contain("GATE_RUN_ENDED");
    }

    [Fact]
    public async Task ApprovalGate_Rejection_FailsTheRun_WithoutRetrying()
    {
        const string yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata: { id: reject-e2e, name: "Reject E2E" }
            spec:
              trigger: { type: manual }
              nodes:
                - { id: start, type: trigger }
                - id: approval
                  type: human-gate
                  config: { delivery_channel: Portal }
                  retry: { max_attempts: 3, backoff_seconds: 0, backoff_multiplier: 1 }
                - { id: done, type: end }
              edges:
                - { id: e1, from: start, to: approval }
                - { id: e2, from: approval, to: done }
            """;
        var owner = await RegisterOwnerAsync("e2e-reject");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-reject");
        var instanceId = await PublishAndTriggerAsync(owner, ws, "reject-e2e", yaml, new { });
        (await DriveAsync(ws, instanceId)).Should().Be("Waiting");

        await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/gates/{instanceId}/approval/decide",
            new { decision = "rejected", note = "over budget" });

        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        (await db.WorkflowInstances.AsNoTracking().FirstAsync(x => x.Id == instanceId)).Status.ToString().Should().Be("Failed");
        (await db.GateDecisions.AsNoTracking().CountAsync(g => g.InstanceId == instanceId)).Should().Be(1, "a rejection must not reopen the gate");
    }

    [Fact]
    public async Task ConnectorStep_UsesVaultCredential_AndInterpolatedInputs()
    {
        // A local endpoint stands in for the vendor API (Development allows private egress).
        using var server = new CapturingServer();
        var owner = await RegisterOwnerAsync("e2e-conn");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-conn");

        var cred = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/credentials",
            new { connectorId = "http-rest", name = "Orders API", authType = "BearerToken", value = "s3cr3t-token" });
        cred.StatusCode.Should().Be(HttpStatusCode.OK, await cred.Content.ReadAsStringAsync());
        (await cred.Content.ReadAsStringAsync()).Should().NotContain("s3cr3t-token", "credential values are write-only");

        var yaml = $$$"""
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata: { id: conn-e2e, name: "Connector E2E" }
            spec:
              trigger: { type: manual }
              nodes:
                - { id: start, type: trigger }
                - id: create-po
                  type: action
                  config:
                    connector_id: http-rest
                    operation: POST
                    credential: Orders API
                    inputs:
                      url: "{{{server.Url}}}orders"
                      body: '{"amount": {{ variables.amount }}, "requester": "{{ requester }}"}'
                - { id: done, type: end }
              edges:
                - { id: e1, from: start, to: create-po }
                - { id: e2, from: create-po, to: done }
            """;
        var instanceId = await PublishAndTriggerAsync(owner, ws, "conn-e2e", yaml, new { amount = 1500, requester = "ana@example.com" });

        (await DriveAsync(ws, instanceId)).Should().Be("Completed");

        server.Authorization.Should().Be("Bearer s3cr3t-token");
        server.Body.Should().Be("""{"amount": 1500, "requester": "ana@example.com"}""");

        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var output = await db.WorkflowVariables.AsNoTracking().FirstAsync(v => v.InstanceId == instanceId && v.Name == "create-po.output");
        output.Value.Should().Contain("po-123");
    }

    [Fact]
    public async Task ConnectorStep_RevokedCredential_FailsWithActionableMessage()
    {
        using var server = new CapturingServer();
        var owner = await RegisterOwnerAsync("e2e-revoked");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-revoked");
        var cred = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/credentials",
            new { connectorId = "http-rest", name = "Old Key", authType = "BearerToken", value = "old" });
        var credId = (await DataAsync(cred)).GetProperty("id").GetGuid();
        (await owner.Client.DeleteAsync($"/api/v1/workspaces/{ws}/credentials/{credId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var yaml = $$$"""
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata: { id: revoked-e2e, name: "Revoked" }
            spec:
              trigger: { type: manual }
              nodes:
                - { id: start, type: trigger }
                - id: call
                  type: action
                  config: { connector_id: http-rest, operation: GET, credential: Old Key, inputs: { url: "{{{server.Url}}}x" } }
                - { id: done, type: end }
              edges:
                - { id: e1, from: start, to: call }
                - { id: e2, from: call, to: done }
            """;
        var instanceId = await PublishAndTriggerAsync(owner, ws, "revoked-e2e", yaml, new { });

        (await DriveAsync(ws, instanceId)).Should().Be("Failed");
        server.Authorization.Should().BeNull("a revoked credential must never be sent");
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        (await db.WorkflowInstances.AsNoTracking().FirstAsync(x => x.Id == instanceId)).ErrorMessage
            .Should().Contain("Old Key").And.Contain("Settings → Credentials");
    }

    [Fact]
    public async Task Credentials_List_ShowsAliasesOnly()
    {
        var owner = await RegisterOwnerAsync("e2e-creds");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e-creds");
        await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/credentials",
            new { connectorId = "postgresql", name = "Prod DB", authType = "ConnectionString", value = "Host=db.example.com;Password=hunter2" });

        var wrongType = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/credentials",
            new { connectorId = "postgresql", name = "Bad", authType = "ApiKey", value = "x" });
        wrongType.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var list = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/credentials?connectorId=postgresql");
        var text = await list.Content.ReadAsStringAsync();
        text.Should().Contain("Prod DB").And.NotContain("hunter2");
    }

    [Theory]
    [InlineData(1500, "big")]
    [InlineData(200, "small")]
    public async Task CanvasRouter_TakesOnlyTheMatchingBranch_OrTheDefault(int amount, string expectedEnd)
    {
        // Regression: canvas routers kept conditions on the node (edges only had `via`), so the
        // engine saw unconditional edges and took every branch; `${amount} > 1000` was always true.
        const string yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata: { id: router-e2e, name: "Router" }
            spec:
              trigger: { type: manual }
              nodes:
                - { id: start, type: trigger }
                - id: route
                  type: router
                  config:
                    branches:
                      - { label: Large, condition: "${amount} > 1000" }
                      - { label: Otherwise, condition: "", isDefault: true }
                - { id: big, type: end }
                - { id: small, type: end }
              edges:
                - { id: e1, from: start, to: route }
                - { id: e2, from: route, to: big, via: Large }
                - { id: e3, from: route, to: small, via: Otherwise }
            """;
        var owner = await RegisterOwnerAsync($"e2e-router-{amount}");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", $"e2e-router-{amount}");
        var instanceId = await PublishAndTriggerAsync(owner, ws, "router-e2e", yaml, new { amount });

        (await DriveAsync(ws, instanceId)).Should().Be("Completed");

        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var entered = await db.WorkflowNodeStates.AsNoTracking().Where(n => n.InstanceId == instanceId).Select(n => n.NodeId).ToListAsync();
        entered.Should().Contain(expectedEnd).And.NotContain(expectedEnd == "big" ? "small" : "big");
    }

    /// <summary>Minimal HTTP endpoint on loopback that records the last request.</summary>
    private sealed class CapturingServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();

        public CapturingServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public string Url { get; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                Authorization = ctx.Request.Headers["Authorization"];
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    Body = await reader.ReadToEndAsync();
                var bytes = Encoding.UTF8.GetBytes("""{"id":"po-123"}""");
                ctx.Response.ContentType = "application/json";
                ctx.Response.StatusCode = 201;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
        }
    }
}
