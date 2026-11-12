using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase8;

/// <summary>
/// Empathy View from real runs (§8.2), DNA inheritance (§8.8) and the audit-ready PDF (§8.10), through the API
/// against real Postgres.
/// </summary>
[Collection("api")]
public sealed class DevItemsTests : ApiTestBase
{
    public DevItemsTests(IntegrationApiFixture fixture) : base(fixture) { }

    // The v1 envelope (spec.nodes): how NL generation and the canvas save workflows.
    private const string Weak = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: po-west
          name: "PO approval (West)"
        spec:
          trigger:
            type: manual
          nodes:
            - id: start
              type: Trigger
              label: "Start"
            - id: finance
              type: HumanGate
              label: "Finance approval"
            - id: create-po
              type: Action
              label: "Create PO"
              config:
                connector: "http-rest"
                operation: "request"
            - id: end
              type: End
              label: "End"
          edges:
            - { id: e1, from: start, to: finance }
            - { id: e2, from: finance, to: create-po }
            - { id: e3, from: create-po, to: end }
        """;

    private const string Strong = """
        workflow:
          id: po-east
          version: v1
          name: "PO approval (East)"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          - id: finance
            type: HumanGate
            label: "Finance approval"
            config:
              timeout_hours: 24
              on_timeout: escalate
          - id: create-po
            type: Action
            label: "Create PO"
            config:
              connector: "http-rest"
              operation: "request"
            retry:
              maxAttempts: 3
              backoffSeconds: 30
              backoffMultiplier: 2
          - id: tell
            type: Action
            label: "Tell requester"
            config:
              connector: "email-smtp"
              operation: "send-email"
          - id: end
            type: End
            label: "End"
        edges:
          - { id: e1, from: start, to: finance }
          - { id: e2, from: finance, to: create-po }
          - { id: e3, from: create-po, to: tell }
          - { id: e4, from: tell, to: end }
        """;

    private static string U(string slug) => $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

    private async Task<Guid> CreateWorkflowAsync(HttpClient client, Guid ws, string name, string yaml)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name, slug = U("wf"), yamlContent = yaml, createdByMethod = "Canvas" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task WithDbAsync(Func<FlowAmazDbContext, Task> work)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        await work(db);
        await db.SaveChangesAsync();
    }

    private static WorkflowMetric Hour(Guid ws, Guid wf, int completed, int failed, int hoursAgo) => new()
    {
        WorkspaceId = ws, WorkflowDefinitionId = wf, PeriodHour = DateTime.UtcNow.AddHours(-hoursAgo).Date.AddHours(DateTime.UtcNow.Hour),
        RunsTotal = completed + failed, RunsCompleted = completed, RunsFailed = failed, AvgDurationMs = 3_600_000,
    };

    [Fact]
    public async Task A_weaker_workflow_inherits_patterns_from_a_better_similar_one()
    {
        var owner = await RegisterOwnerAsync(U("dev-inherit"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Purchasing", U("dev-inherit-ws"));
        var weak = await CreateWorkflowAsync(owner.Client, ws, "PO approval (West)", Weak);
        var strong = await CreateWorkflowAsync(owner.Client, ws, "PO approval (East)", Strong);
        await WithDbAsync(db =>
        {
            db.WorkflowMetrics.AddRange(Hour(ws, weak, 6, 4, 30), Hour(ws, strong, 10, 0, 30));
            return Task.CompletedTask;
        });

        var report = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{weak}/inheritance"));
        report.GetProperty("performance").GetProperty("successPercent").GetInt32().Should().Be(60);
        var kinds = report.GetProperty("suggestions").EnumerateArray().Select(s => s.GetProperty("kind").GetString()).ToList();
        kinds.Should().BeEquivalentTo(["gate-timeout", "action-retry", "outcome-notification"]);
        report.GetProperty("suggestions")[0].GetProperty("evidence").GetString().Should().Contain("100% of its runs succeed, against 60% here");

        var apply = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/{weak}/inheritance/apply",
            new { kind = "gate-timeout", fromWorkflowId = strong });
        apply.StatusCode.Should().Be(HttpStatusCode.OK, await apply.Content.ReadAsStringAsync());
        var saved = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{weak}"));
        saved.GetProperty("yamlContent").GetString().Should().Contain("timeout_hours: 24").And.Contain("on_timeout: escalate")
            .And.Contain("spec:", "the v1 envelope is kept");

        var retry = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/{weak}/inheritance/apply",
            new { kind = "action-retry", fromWorkflowId = strong });
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await DataAsync(retry)).GetProperty("yamlContent").GetString().Should().Contain("maxAttempts: 3");

        var after = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{weak}/inheritance"));
        after.GetProperty("suggestions").EnumerateArray().Select(s => s.GetProperty("kind").GetString()).Should().NotContain("gate-timeout");

        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/{weak}/inheritance/apply",
            new { kind = "outcome-notification", fromWorkflowId = strong })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Another organisation can't read or change it.
        var other = await RegisterOwnerAsync(U("dev-inherit-other"));
        (await other.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{weak}/inheritance")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_workflow_without_enough_runs_says_so()
    {
        var owner = await RegisterOwnerAsync(U("dev-inherit-new"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Purchasing", U("dev-inherit-new-ws"));
        var weak = await CreateWorkflowAsync(owner.Client, ws, "PO approval (West)", Weak);

        var report = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{weak}/inheritance"));
        report.GetProperty("suggestions").GetArrayLength().Should().Be(0);
        report.GetProperty("message").GetString().Should().Contain("0 finished runs").And.Contain("After 5");
    }

    [Fact]
    public async Task The_empathy_view_measures_real_runs_and_ignores_test_runs()
    {
        var owner = await RegisterOwnerAsync(U("dev-empathy"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Onboarding", U("dev-empathy-ws"));
        var wf = await CreateWorkflowAsync(owner.Client, ws, "PO approval (East)", Strong);
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{wf}/publish", null)).EnsureSuccessStatusCode();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", new { workflowDefinitionId = wf });
            trigger.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted, HttpStatusCode.Created);
            ids.Add((await DataAsync(trigger)).GetProperty("instanceId").GetGuid());
        }

        // The worker is off in the test host: write the finished runs as the engine would.
        var t0 = DateTime.UtcNow.AddDays(-3);
        await WithDbAsync(async db =>
        {
            var runs = await db.WorkflowInstances.Where(i => ids.Contains(i.Id)).ToListAsync();
            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                run.Status = InstanceStatus.Completed;
                run.StartedAt = t0;
                run.CompletedAt = t0.AddHours(48);
                run.IsTest = i == 2; // the third is a test run
                db.GateDecisions.Add(new GateDecision
                {
                    WorkspaceId = ws, InstanceId = run.Id, NodeId = "finance", Decision = GateDecisionStatus.Approved,
                    CreatedAt = t0.AddHours(1), DecidedAt = t0.AddHours(40), ExpiresAt = t0.AddHours(72),
                });
                db.WorkflowEvents.Add(new WorkflowEvent
                {
                    WorkspaceId = ws, InstanceId = run.Id, SequenceNumber = 900, EventType = "NodeCompleted", NodeId = "tell",
                    NodeType = "Action", OccurredAt = t0.AddHours(47),
                });
            }
        });

        // CreatedAt is stamped on save; set when the approval was really requested.
        await WithDbAsync(db => db.GateDecisions.Where(g => ids.Contains(g.InstanceId))
            .ExecuteUpdateAsync(u => u.SetProperty(g => g.CreatedAt, t0.AddHours(1))));

        var empathy = await DataAsync(await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/{wf}/empathy", new { yamlContent = (string?)null }));
        var runsJson = empathy.GetProperty("runs");
        runsJson.GetProperty("runsAnalysed").GetInt32().Should().Be(2, "test runs don't count");
        runsJson.GetProperty("avgDaysToOutcome").GetDouble().Should().Be(2);
        runsJson.GetProperty("medianLongestSilenceHours").GetDouble().Should().Be(47);
        runsJson.GetProperty("percentRunsOutcomeNotified").GetInt32().Should().Be(100);
        runsJson.GetProperty("slowestGateNodeId").GetString().Should().Be("finance");
        empathy.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("label").GetString()).Should().Contain("Long silence");
    }

    [Fact]
    public async Task The_audit_record_downloads_as_a_pdf()
    {
        var owner = await RegisterOwnerAsync(U("dev-pdf"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Finance", U("dev-pdf-ws"));
        var wf = await CreateWorkflowAsync(owner.Client, ws, "PO approval (East)", Strong);
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{wf}/publish", null)).EnsureSuccessStatusCode();
        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", new { workflowDefinitionId = wf });
        var instance = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();
        var url = $"/api/v1/workspaces/{ws}/instances/{instance}/narrative/download?audience=auditor";

        var pdf = await owner.Client.PostAsync(url, null);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(bytes))
            string.Join(" ", doc.GetPages().Select(p => p.Text)).Should().Contain("Compliance Record").And.Contain("PO approval (East)");

        (await owner.Client.PostAsync(url + "&format=md", null)).Content.Headers.ContentType!.MediaType.Should().Be("text/markdown");
        (await owner.Client.PostAsync(url + "&format=docx", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
