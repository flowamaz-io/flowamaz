using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Workspace;

[Collection("api")]
public class MemberApiTests : ApiTestBase
{
    public MemberApiTests(IntegrationApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Add_member_with_unknown_email_returns_404()
    {
        var owner = await RegisterOwnerAsync("mem-unknown");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "mem-ws");

        var add = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/members",
            new { email = "ghost@nowhere.test", role = WorkspaceRole.Operator });

        add.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MessageAsync(add)).Should().Contain("register first");
    }

    [Fact]
    public async Task Add_member_who_is_already_a_member_returns_409()
    {
        var owner = await RegisterOwnerAsync("mem-dup");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "dup-ws");

        // The owner is already the workspace Admin.
        var add = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/members",
            new { email = owner.Email, role = WorkspaceRole.Operator });

        add.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_members_returns_the_owner_as_admin()
    {
        var owner = await RegisterOwnerAsync("mem-list");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "list-ws");

        var list = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/members");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await DataAsync(list);
        var members = data.GetProperty("data");
        members.GetArrayLength().Should().Be(1);
        members[0].GetProperty("email").GetString().Should().Be(owner.Email);
        members[0].GetProperty("role").GetString().Should().Be("Admin");
    }

    [Fact]
    public async Task Viewer_cannot_add_members_403()
    {
        var owner = await RegisterOwnerAsync("mem-viewer");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "WS", "viewer-ws");

        // Seed a second org user with the Viewer role directly (no invite endpoint in Phase 1).
        Guid viewerId;
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var viewer = new OrgUser
            {
                OrgId = owner.OrgId,
                Email = "viewer@mem-viewer.test",
                Name = "Vic Viewer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Viewer1Pass", 12),
                IsActive = true,
            };
            db.OrgUsers.Add(viewer);
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspaceId,
                OrgUserId = viewer.Id,
                Role = WorkspaceRole.Viewer,
                IsActive = true,
            });
            await db.SaveChangesAsync();
            viewerId = viewer.Id;
        }

        // Log in as the viewer.
        var viewerClient = Fixture.NewClient();
        var login = await viewerClient.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "viewer@mem-viewer.test", password = "Viewer1Pass", orgSlug = owner.OrgSlug });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await DataAsync(login)).GetProperty("accessToken").GetString();
        viewerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var add = await viewerClient.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/members",
            new { email = "ghost@nowhere.test", role = WorkspaceRole.Operator });

        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        viewerId.Should().NotBeEmpty();
    }
}
