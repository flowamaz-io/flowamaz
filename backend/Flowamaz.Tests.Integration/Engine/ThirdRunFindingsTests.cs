using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
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
/// Regression coverage for the third Chrome E2E run: the plain-English prompt's example must actually run
/// (routing on gate decisions and branch conditions), and audit rows for API key revocation and webhook
/// creation name the user.
/// </summary>
[Collection("api")]
public class ThirdRunFindingsTests : ApiTestBase
{
    public ThirdRunFindingsTests(IntegrationApiFixture fixture) : base(fixture) { }

    private async Task DriveAsync(Guid ws, Guid instanceId)
    {
        var worker = new OrchestratorWorker(
            Fixture.Services.GetRequiredService<ITaskQueue>(),
            Fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            Fixture.Services.GetRequiredService<IConfiguration>(),
            NullLogger<OrchestratorWorker>.Instance);
        for (var i = 0; i < 20; i++)
        {
            using (var scope = Fixture.Services.CreateScope())
                await worker.ProcessInstanceAsync(scope.ServiceProvider, ws, instanceId, CancellationToken.None);
            using var read = Fixture.Services.CreateScope();
            var db = read.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var status = (await db.WorkflowInstances.AsNoTracking().FirstAsync(x => x.Id == instanceId)).Status.ToString();
            if (status is "Completed" or "Failed" or "Waiting" or "Cancelled") return;
        }
    }

    private static async Task<List<string>> VisitedAsync(Owner owner, Guid ws, Guid instanceId)
    {
        var detail = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/instances/{instanceId}"));
        return detail.GetProperty("nodeStates").EnumerateArray().Select(n => n.GetProperty("nodeId").GetString()!).ToList();
    }

    [Fact]
    public async Task Prompt_example_routes_on_gate_decisions_and_branch_conditions()
    {
        var yaml = Regex.Match(NlYamlGenerationService.SystemPrompt,
            @"EXAMPLE 1[^\n]*\n(apiVersion:.*?)(?=\n\s*\n)", RegexOptions.Singleline).Groups[1].Value;
        var owner = await RegisterOwnerAsync("e2e3-prompt");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e3-prompt");
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Purchase Approval", slug = "purchase-approval", yamlContent = yaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        var publish = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());

        async Task<Guid> RunAsync(int amount)
        {
            var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", new
            {
                workflowDefinitionId = workflowId,
                payload = JsonSerializer.Serialize(new { amount, requester_email = "a@b.test", item = "Laptop" }),
            });
            trigger.StatusCode.Should().Be(HttpStatusCode.OK, await trigger.Content.ReadAsStringAsync());
            var id = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();
            await DriveAsync(ws, id);
            var decide = await owner.Client.PostAsJsonAsync(
                $"/api/v1/workspaces/{ws}/gates/{id}/manager-approval/decide", new { decision = "approved", note = "ok" });
            decide.StatusCode.Should().Be(HttpStatusCode.OK, await decide.Content.ReadAsStringAsync());
            await DriveAsync(ws, id);
            return id;
        }

        // Over 1000: manager approves, then the "Over 1000" branch asks Finance.
        var big = await VisitedAsync(owner, ws, await RunAsync(1500));
        big.Should().Contain(["manager-decision", "amount-check", "finance-approval"]).And.NotContain("create-purchase-order");

        // Up to 1000: straight to the purchase order (an action with no connector yet, so the run stops there).
        var small = await VisitedAsync(owner, ws, await RunAsync(500));
        small.Should().Contain(["amount-check", "create-purchase-order"]).And.NotContain("finance-approval");
    }

    [Fact]
    public async Task Api_key_revocation_and_webhook_creation_name_the_user()
    {
        var owner = await RegisterOwnerAsync("e2e3-audit");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "e2e3-audit");
        const string yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata: { id: hook-wf, name: Hook }
            spec:
              trigger: { type: webhook }
              nodes:
                - { id: start, type: trigger, label: Start }
                - { id: done, type: end, label: Done }
              edges:
                - { id: e1, from: start, to: done }
            """;
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Hook", slug = "hook-wf", yamlContent = yaml, createdByMethod = "Canvas" });
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();
        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/webhooks",
            new { workflowDefinitionId = workflowId, description = "Orders feed" })).EnsureSuccessStatusCode();

        var envs = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/environments"));
        var envId = envs.EnumerateArray().First(e => e.GetProperty("name").GetString()!.Equals("Production", StringComparison.OrdinalIgnoreCase))
            .GetProperty("id").GetGuid();
        var key = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/api-keys",
            new { name = "Read only", environmentId = envId, scopes = new[] { "workflows:read" }, expiresAt = (DateTime?)null });
        key.StatusCode.Should().Be(HttpStatusCode.OK, await key.Content.ReadAsStringAsync());
        var keyId = (await DataAsync(key)).GetProperty("id").GetGuid();
        (await owner.Client.DeleteAsync($"/api/v1/workspaces/{ws}/api-keys/{keyId}")).IsSuccessStatusCode.Should().BeTrue();

        foreach (var eventType in new[] { "webhook.created", "api_key.revoked" })
        {
            JsonElement? row = null;
            for (var attempt = 0; attempt < 25 && row is null; attempt++)
            {
                var data = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/audit?page_size=100"));
                row = data.GetProperty("data").EnumerateArray().Where(r => r.GetProperty("eventType").GetString() == eventType)
                    .Select(r => (JsonElement?)r.Clone()).FirstOrDefault();
                if (row is null) await Task.Delay(120);
            }
            row.Should().NotBeNull(eventType);
            row!.Value.GetProperty("actor").GetString().Should().Contain(owner.Email, eventType);
            row.Value.GetProperty("summary").GetString().Should().NotStartWith("A user", eventType);
        }
    }
}
