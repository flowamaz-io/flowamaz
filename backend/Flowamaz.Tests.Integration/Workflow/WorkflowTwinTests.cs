using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using System.Net.Http.Headers;
using Flowamaz.Application.Workflow.Twin;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Workflow;

/// <summary>
/// Workflow Twin end to end: run on demand, latest result and list badges, roles, workspace isolation, the scheduled
/// pass over live workflows, and that a simulation never creates a real run.
/// </summary>
[Collection("api")]
public sealed class WorkflowTwinTests : ApiTestBase
{
    public WorkflowTwinTests(IntegrationApiFixture fixture) : base(fixture) { }

    private static string U(string slug) => $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

    private const string PurchaseYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: purchase, name: Purchase approval }
        spec:
          variables:
            amount: { type: number, required: true }
          nodes:
            - { id: start, type: trigger }
            - id: size
              type: router
              label: Amount check
              config:
                branches:
                  - { label: Small, condition: "{{ amount }} < 5000" }
                  - { label: Large, condition: "{{ amount }} > 5000" }
            - { id: manager, type: human-gate, label: Manager approval }
            - { id: decided, type: router, label: Approved? }
            - { id: done, type: end }
            - { id: refused, type: end }
          edges:
            - { from: start, to: size }
            - { from: size, to: done, via: Small }
            - { from: size, to: manager, via: Large }
            - { from: manager, to: decided }
            - { from: decided, to: done, via: approved }
            - { from: decided, to: refused, via: rejected }
        """;

    private async Task<(Owner Owner, Guid Ws, Guid WorkflowId)> SetupAsync(string slug, bool publish = false)
    {
        var owner = await RegisterOwnerAsync(slug);
        var ws = await CreateWorkspaceAsync(owner.Client, "Twin", slug);
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Purchase", slug = "purchase", yamlContent = PurchaseYaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var id = (await DataAsync(create)).GetProperty("id").GetGuid();
        if (publish)
            (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{id}/publish", null)).EnsureSuccessStatusCode();
        return (owner, ws, id);
    }

    [Fact]
    public async Task Run_finds_the_boundary_break_and_the_result_is_kept_for_the_badge()
    {
        var (owner, ws, id) = await SetupAsync(U("twin-run"));

        var before = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin"));
        before.GetProperty("run").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        before.GetProperty("hasProductionVersion").GetBoolean().Should().BeFalse();

        var run = await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin", null);
        run.StatusCode.Should().Be(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        var result = await DataAsync(run);
        result.GetProperty("status").GetString().Should().Be("breaks");
        result.GetProperty("versionLabel").GetString().Should().Be("draft");
        result.GetProperty("casesRun").GetInt32().Should().BeGreaterThan(10);
        var findings = result.GetProperty("findings").EnumerateArray().ToList();
        findings.Should().Contain(f => f.GetProperty("title").GetString() == "Breaks when amount = 5000"
            && f.GetProperty("severity").GetString() == "break");

        var latest = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin"));
        latest.GetProperty("run").GetProperty("id").GetGuid().Should().Be(result.GetProperty("id").GetGuid());

        var summary = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/twin/summary"));
        summary.EnumerateArray().Should().ContainSingle(s => s.GetProperty("workflowDefinitionId").GetGuid() == id
            && s.GetProperty("status").GetString() == "breaks" && s.GetProperty("breakCount").GetInt32() > 0);

        // Simulation never creates a real run.
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        (await db.WorkflowInstances.CountAsync(i => i.WorkflowDefinitionId == id)).Should().Be(0);
    }

    [Fact]
    public async Task Other_workspaces_cannot_see_or_run_it()
    {
        var (_, ws, id) = await SetupAsync(U("twin-a"));
        var other = await RegisterOwnerAsync(U("twin-b"));

        var get = await other.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin");
        get.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        var post = await other.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin", null);
        post.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_workflow_is_404()
    {
        var (owner, ws, _) = await SetupAsync(U("twin-404"));
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{Guid.NewGuid()}/twin", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Scheduled_pass_simulates_live_workflows_once_per_version_and_day()
    {
        var (owner, ws, id) = await SetupAsync(U("twin-due"), publish: true);
        // A real run's payload becomes an extra base input.
        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = id, payload = "{\"amount\":5000}" })).EnsureSuccessStatusCode();

        using (var scope = Fixture.Services.CreateScope())
        {
            var twin = scope.ServiceProvider.GetRequiredService<WorkflowTwinService>();
            await twin.RunDueAsync(500);
        }
        var latest = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin"));
        latest.GetProperty("hasProductionVersion").GetBoolean().Should().BeTrue();
        var run = latest.GetProperty("run");
        run.GetProperty("trigger").GetString().Should().Be("scheduled");
        run.GetProperty("versionLabel").GetString().Should().Be("production");
        // The real payload (amount 5000) is itself one of the inputs that break it.
        run.GetProperty("findings").EnumerateArray().Should().Contain(f =>
            f.GetProperty("title").GetString() == "Breaks for a recent real payload (#1)");

        // Up to date now: the next pass leaves it alone.
        using (var scope = Fixture.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<Flowamaz.Core.Interfaces.Repositories.IWorkflowTwinRepository>();
            var due = await repo.GetDueAsync(DateTime.UtcNow.AddHours(-24), 500);
            due.Should().NotContain(d => d.WorkflowDefinitionId == id);
        }
    }

    [Fact]
    public async Task Only_the_newest_runs_are_kept()
    {
        var (owner, ws, id) = await SetupAsync(U("twin-prune"));
        for (var i = 0; i < WorkflowTwinService.RunsKeptPerWorkflow + 2; i++)
            (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin", null)).EnsureSuccessStatusCode();

        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        (await db.WorkflowTwinRuns.CountAsync(r => r.WorkflowDefinitionId == id)).Should().Be(WorkflowTwinService.RunsKeptPerWorkflow);
    }

    /// <summary>A person in the org with the given role in the workspace, signed in with a fresh access token.</summary>
    private async Task<HttpClient> MemberAsync(Guid orgId, Guid ws, WorkspaceRole role)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var user = new OrgUser { OrgId = orgId, Email = $"{role}-{Guid.NewGuid():N}@twin.test", Name = role.ToString(), PasswordHash = string.Empty, IsActive = true };
        db.OrgUsers.Add(user);
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = ws, OrgUserId = user.Id, Role = role, IsActive = true });
        await db.SaveChangesAsync();
        var slug = await db.Organisations.Where(o => o.Id == orgId).Select(o => o.Slug).SingleAsync();
        var memberships = await scope.ServiceProvider.GetRequiredService<IWorkspaceMemberRepository>().GetActiveMembershipsForUserAsync(user.Id);
        var token = scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateAccessToken(user, slug, memberships);
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Viewers_see_results_but_cannot_run_it_or_see_real_payload_values()
    {
        var (owner, ws, id) = await SetupAsync(U("twin-view"), publish: true);
        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = id, payload = "{\"amount\":5000}" })).EnsureSuccessStatusCode();
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin", null)).EnsureSuccessStatusCode();

        var viewer = await MemberAsync(owner.OrgId, ws, WorkspaceRole.Viewer);
        (await viewer.PostAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var seen = await DataAsync(await viewer.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin"));
        var real = seen.GetProperty("run").GetProperty("findings").EnumerateArray()
            .Single(f => f.GetProperty("fromRealPayload").GetBoolean());
        real.GetProperty("example").EnumerateObject().Should().OnlyContain(p => p.Value.GetString() == WorkflowTwinService.HiddenValue);

        var operatorClient = await MemberAsync(owner.OrgId, ws, WorkspaceRole.Operator);
        var full = await DataAsync(await operatorClient.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin"));
        full.GetProperty("run").GetProperty("findings").EnumerateArray()
            .Single(f => f.GetProperty("fromRealPayload").GetBoolean())
            .GetProperty("example").GetProperty("amount").GetString().Should().Be("5000");
        (await operatorClient.PostAsync($"/api/v1/workspaces/{ws}/workflows/{id}/twin", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
