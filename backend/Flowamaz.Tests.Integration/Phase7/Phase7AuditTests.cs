using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase7;

/// <summary>
/// Immutable audit trail over the real server (prompt 07-02/07-07). Creating a workflow and
/// triggering an instance record audit events (fire-and-forget, so we poll); the workspace feed is
/// Admin-gated and a non-admin is rejected with 403; CSV export streams the correct content-type.
/// </summary>
[Collection("api")]
public class Phase7AuditTests : ApiTestBase
{
    public Phase7AuditTests(IntegrationApiFixture fixture) : base(fixture) { }

    private const string Yaml = """
        workflow: { id: aud, version: v1, name: Aud }
        nodes:
          - { id: start, type: Trigger }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: done }
        """;

    /// <summary>Polls the workspace audit feed until an event of the given type appears (or times out).</summary>
    private static async Task<JsonElement?> PollForEventAsync(HttpClient client, Guid workspaceId, string eventType)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var resp = await client.GetAsync($"/api/v1/workspaces/{workspaceId}/audit?page_size=100");
            resp.EnsureSuccessStatusCode();
            var data = await DataAsync(resp);
            foreach (var row in data.GetProperty("data").EnumerateArray())
            {
                if (row.GetProperty("eventType").GetString() == eventType)
                    return row.Clone();
            }
            await Task.Delay(120);
        }
        return null;
    }

    private async Task<Guid> CreateWorkflowAsync(Owner owner, Guid workspaceId, string slug)
    {
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/workflows",
            new { name = "Aud", slug, yamlContent = Yaml, createdByMethod = "NaturalLanguage" });
        create.EnsureSuccessStatusCode();
        return (await DataAsync(create)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Creating_a_workflow_records_an_audit_event()
    {
        var owner = await RegisterOwnerAsync("p7-aud-create");
        var ws = await CreateWorkspaceAsync(owner.Client, "Aud", "p7-aud-c");
        await CreateWorkflowAsync(owner, ws, "aud-flow");

        var ev = await PollForEventAsync(owner.Client, ws, "workflow.created");

        ev.Should().NotBeNull("the audit event is recorded fire-and-forget shortly after creation");
        ev!.Value.GetProperty("resourceType").GetString().Should().Be("workflow");
    }

    [Fact]
    public async Task Triggering_an_instance_records_an_audit_event_with_actor()
    {
        var owner = await RegisterOwnerAsync("p7-aud-trigger");
        var ws = await CreateWorkspaceAsync(owner.Client, "Aud", "p7-aud-t");
        var workflowId = await CreateWorkflowAsync(owner, ws, "aud-trigger-flow");
        (await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();

        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances",
            new { workflowDefinitionId = workflowId });
        trigger.EnsureSuccessStatusCode();

        var ev = await PollForEventAsync(owner.Client, ws, "instance.started");

        ev.Should().NotBeNull();
        // A manual trigger is recorded as a user actor against the instance resource. (The orchestrator
        // is a system-level service so it stamps actor_type=user but not actor_user_id; the request IP
        // is captured from the ambient HttpContext.)
        ev!.Value.GetProperty("actorType").GetString().Should().Be("user");
        ev.Value.GetProperty("resourceType").GetString().Should().Be("instance");
        ev.Value.TryGetProperty("ipAddress", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Export_csv_returns_csv_content_type_and_header_row()
    {
        var owner = await RegisterOwnerAsync("p7-aud-export");
        var ws = await CreateWorkspaceAsync(owner.Client, "Aud", "p7-aud-e");
        await CreateWorkflowAsync(owner, ws, "aud-export-flow");
        await PollForEventAsync(owner.Client, ws, "workflow.created");

        var resp = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/audit/export");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().StartWith("timestamp,event_type,actor_type,actor,action,resource_type,resource,ip_address");
    }

    [Fact]
    public async Task Non_admin_member_cannot_read_workspace_audit_feed()
    {
        var owner = await RegisterOwnerAsync("p7-aud-rbac");
        var ws = await CreateWorkspaceAsync(owner.Client, "Aud", "p7-aud-r");

        // Seed a Viewer (non-admin) workspace member and log them in.
        var viewerId = await SeedOrgUserAsync(owner.OrgId, "viewer@p7-aud-rbac.test", "ViewerPass1!");
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = ws, OrgUserId = viewerId, Role = WorkspaceRole.Viewer, IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var viewer = Fixture.NewClient();
        var login = await viewer.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "viewer@p7-aud-rbac.test", password = "ViewerPass1!", orgSlug = owner.OrgSlug });
        var token = (await DataAsync(login)).GetProperty("accessToken").GetString();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await viewer.GetAsync($"/api/v1/workspaces/{ws}/audit");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> SeedOrgUserAsync(Guid orgId, string email, string password)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var user = new OrgUser
        {
            OrgId = orgId,
            Email = email,
            Name = "Seeded Viewer",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            IsActive = true,
        };
        db.OrgUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
