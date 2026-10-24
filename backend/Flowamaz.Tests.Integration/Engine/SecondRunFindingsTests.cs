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
/// Regression coverage for the second Chrome E2E run: the Gates "All" tab lists decided approvals,
/// the overview's trigger type and description follow the saved YAML, and audit rows name the user
/// who acted and the credential that was revoked.
/// </summary>
[Collection("api")]
public class SecondRunFindingsTests : ApiTestBase
{
    public SecondRunFindingsTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string GateYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: second-run-gate, name: "Second run gate" }
        spec:
          trigger: { type: manual }
          nodes:
            - { id: start, type: trigger, label: Start }
            - id: approval
              type: human-gate
              label: Approval
              config: { delivery_channel: Portal, timeout_hours: 24, on_timeout: auto-reject }
            - { id: done, type: end, label: Done }
          edges:
            - { id: e1, from: start, to: approval }
            - { id: e2, from: approval, to: done }
        """;

    private async Task<string> DriveAsync(Guid ws, Guid instanceId)
    {
        var worker = new OrchestratorWorker(
            Fixture.Services.GetRequiredService<ITaskQueue>(),
            Fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            Fixture.Services.GetRequiredService<IConfiguration>(),
            NullLogger<OrchestratorWorker>.Instance);
        var status = "Pending";
        for (var i = 0; i < 15; i++)
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

    private static async Task<List<JsonElement>> ItemsAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = await DataAsync(response);
        var array = data.ValueKind == JsonValueKind.Array ? data : data.GetProperty("data");
        return array.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static async Task<JsonElement?> PollAuditAsync(HttpClient client, Guid ws, string eventType)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var data = await DataAsync(await client.GetAsync($"/api/v1/workspaces/{ws}/audit?page_size=100"));
            foreach (var row in data.GetProperty("data").EnumerateArray())
                if (row.GetProperty("eventType").GetString() == eventType) return row.Clone();
            await Task.Delay(120);
        }
        return null;
    }

    [Fact]
    public async Task Gates_all_lists_decided_and_pending_while_default_lists_only_pending()
    {
        var owner = await RegisterOwnerAsync("e2e2-gates-all");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e2-gates-all");
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Gate", slug = "second-run-gate", yamlContent = GateYaml, createdByMethod = "Canvas" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();

        async Task<Guid> RunToGateAsync()
        {
            var t = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
                new { workflowDefinitionId = workflowId, payload = "{}" });
            t.StatusCode.Should().Be(HttpStatusCode.OK, await t.Content.ReadAsStringAsync());
            var id = (await DataAsync(t)).GetProperty("instanceId").GetGuid();
            (await DriveAsync(ws, id)).Should().Be("Waiting");
            return id;
        }
        var decided = await RunToGateAsync();
        var pending = await RunToGateAsync();

        var decide = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/gates/{decided}/approval/decide", new { decision = "approved", note = "ok" });
        decide.StatusCode.Should().Be(HttpStatusCode.OK, await decide.Content.ReadAsStringAsync());

        var queue = await ItemsAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/gates"));
        queue.Select(g => g.GetProperty("instanceId").GetGuid()).Should().Equal(pending);

        var all = await ItemsAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/gates?status=all"));
        all.Select(g => g.GetProperty("instanceId").GetGuid()).Should().BeEquivalentTo([decided, pending]);
        all.Single(g => g.GetProperty("instanceId").GetGuid() == decided)
            .GetProperty("decision").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task Saving_yaml_updates_the_overview_trigger_type_and_description()
    {
        var owner = await RegisterOwnerAsync("e2e2-sync");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e2-sync");
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Gate", slug = "second-run-sync", yamlContent = GateYaml, createdByMethod = "Canvas" });
        var created = await DataAsync(create);
        created.GetProperty("triggerType").GetString().Should().Be("Manual");
        var id = created.GetProperty("id").GetGuid();

        var formYaml = GateYaml
            .Replace("metadata: { id: second-run-gate, name: \"Second run gate\" }",
                "metadata: { id: second-run-gate, name: \"Second run gate\", description: \"Approves loan applications\" }")
            .Replace("trigger: { type: manual }", "trigger: { type: form }");
        var update = await owner.Client.PutAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/{id}", new { yamlContent = formYaml });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());

        var detail = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}"));
        detail.GetProperty("triggerType").GetString().Should().Be("Form");
        detail.GetProperty("description").GetString().Should().Be("Approves loan applications");
    }

    [Fact]
    public async Task Audit_rows_name_the_acting_user_and_the_revoked_credential()
    {
        var owner = await RegisterOwnerAsync("e2e2-audit");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e2-audit");
        var cred = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/credentials",
            new { connectorId = "http-rest", name = "Orders API", authType = "BearerToken", value = "s3cr3t-token" });
        cred.StatusCode.Should().Be(HttpStatusCode.OK, await cred.Content.ReadAsStringAsync());
        var credId = (await DataAsync(cred)).GetProperty("id").GetGuid();

        var revoke = await owner.Client.DeleteAsync($"/api/v1/workspaces/{ws}/credentials/{credId}");
        revoke.IsSuccessStatusCode.Should().BeTrue(await revoke.Content.ReadAsStringAsync());

        var ev = await PollAuditAsync(owner.Client, ws, "credential.revoked");
        ev.Should().NotBeNull();
        ev!.Value.GetProperty("resource").GetString().Should().Be("Orders API");
        ev.Value.GetProperty("actor").GetString().Should().Contain(owner.Email);
        ev.Value.GetProperty("summary").GetString().Should().Contain("credential 'Orders API'").And.NotStartWith("A user");
        ev.Value.ToString().Should().NotContain("s3cr3t-token");
    }

    [Fact]
    public async Task Draw_on_canvas_skeleton_has_no_yaml_anchors()
    {
        var owner = await RegisterOwnerAsync("e2e2-skeleton");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e2-skeleton");
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create",
            new { name = "Loan Approval UI", method = "canvas" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var body = await DataAsync(create);
        var id = body.GetProperty("workflowId").GetGuid();

        var yaml = (await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}")))
            .GetProperty("yamlContent").GetString();
        // nodes and edges used to share one empty array, so YamlDotNet wrote "&o0" / "*o0".
        yaml.Should().Contain("nodes: []").And.Contain("edges: []");
        yaml.Should().NotContain("&o").And.NotContain("*o");
    }
}
