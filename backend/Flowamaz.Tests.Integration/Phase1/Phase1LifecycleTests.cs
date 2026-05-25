using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase1;

/// <summary>
/// End-to-end Phase 1 lifecycle against the real API + Postgres + Redis: the full auth/workspace/
/// member journey, workspace isolation, the API-key lifecycle, RBAC enforcement, and the Redis
/// rate limiter. Reuses the shared "api" collection fixture.
/// </summary>
[Collection("api")]
public class Phase1LifecycleTests : ApiTestBase
{
    public Phase1LifecycleTests(IntegrationApiFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedOrgUserAsync(Guid orgId, string email, string password)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var user = new OrgUser
        {
            OrgId = orgId,
            Email = email,
            Name = "Seeded User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            IsActive = true,
        };
        db.OrgUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> ProductionEnvironmentIdAsync(Guid workspaceId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var env = await db.WorkspaceEnvironments
            .FirstAsync(e => e.WorkspaceId == workspaceId && e.Name == WorkspaceEnvironmentType.Production);
        return env.Id;
    }

    [Fact]
    public async Task Full_registration_to_logout_lifecycle()
    {
        var owner = await RegisterOwnerAsync("p1-life");

        // Refresh works while the session cookie is valid.
        (await owner.Client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var workspaceId = await CreateWorkspaceAsync(owner.Client, "Life WS", "p1-life-ws");

        // Seed a second org user, then add → list → remove them.
        var memberId = await SeedOrgUserAsync(owner.OrgId, "member@p1-life.test", "MemberPass1");
        var add = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/members",
            new { email = "member@p1-life.test", role = WorkspaceRole.Designer });
        add.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(add)).GetProperty("role").GetString().Should().Be("Designer");

        var listBefore = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/members");
        (await DataAsync(listBefore)).GetProperty("data").GetArrayLength().Should().Be(2);

        var remove = await owner.Client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/members/{memberId}");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfter = await owner.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/members");
        (await DataAsync(listAfter)).GetProperty("data").GetArrayLength().Should().Be(1);

        (await owner.Client.PostAsync("/api/v1/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.Client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Workspace_isolation_org_b_cannot_read_org_a_workspace()
    {
        var ownerA = await RegisterOwnerAsync("p1-iso-a");
        var workspaceId = await CreateWorkspaceAsync(ownerA.Client, "A WS", "p1-iso-a-ws");

        var ownerB = await RegisterOwnerAsync("p1-iso-b");
        (await ownerB.Client.GetAsync($"/api/v1/workspaces/{workspaceId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Api_key_full_lifecycle_create_validate_scope_revoke()
    {
        var owner = await RegisterOwnerAsync("p1-key");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "Key WS", "p1-key-ws");
        var envId = await ProductionEnvironmentIdAsync(workspaceId);

        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/api-keys",
            new { name = "ci", environmentId = envId, scopes = new[] { "workflows:read" }, expiresAt = (DateTime?)null });
        var created = await DataAsync(create);
        var keyId = created.GetProperty("id").GetGuid();
        var plainKey = created.GetProperty("plainKey").GetString()!;

        using (var scope = Fixture.Services.CreateScope())
        {
            var keys = scope.ServiceProvider.GetRequiredService<IWorkspaceApiKeyService>();
            var authz = scope.ServiceProvider.GetRequiredService<IWorkspaceAuthorizationService>();

            var validation = await keys.ValidateApiKeyAsync(plainKey);
            validation.Should().NotBeNull();
            authz.ValidateApiKeyScope(validation!.Scopes, "workflows:read").Should().BeTrue();
            authz.ValidateApiKeyScope(validation.Scopes, "instances:write").Should().BeFalse();
        }

        (await owner.Client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/api-keys/{keyId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = Fixture.Services.CreateScope())
        {
            var keys = scope.ServiceProvider.GetRequiredService<IWorkspaceApiKeyService>();
            (await keys.ValidateApiKeyAsync(plainKey)).Should().BeNull();
        }
    }

    [Fact]
    public async Task Rbac_viewer_cannot_perform_admin_action()
    {
        var owner = await RegisterOwnerAsync("p1-rbac");
        var workspaceId = await CreateWorkspaceAsync(owner.Client, "RBAC WS", "p1-rbac-ws");

        var viewerId = await SeedOrgUserAsync(owner.OrgId, "viewer@p1-rbac.test", "ViewerPass1");
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspaceId, OrgUserId = viewerId, Role = WorkspaceRole.Viewer, IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var viewerClient = Fixture.NewClient();
        var login = await viewerClient.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "viewer@p1-rbac.test", password = "ViewerPass1", orgSlug = owner.OrgSlug });
        var token = (await DataAsync(login)).GetProperty("accessToken").GetString();
        viewerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var add = await viewerClient.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/members",
            new { email = "whoever@p1-rbac.test", role = WorkspaceRole.Operator });
        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rate_limiter_counts_then_blocks_at_the_limit()
    {
        await Fixture.ResetRedisAsync();
        using var scope = Fixture.Services.CreateScope();
        var rateLimit = scope.ServiceProvider.GetRequiredService<IRateLimitService>();
        var key = $"phase1-test:{Guid.NewGuid():N}";

        for (var i = 1; i <= 3; i++)
        {
            (await rateLimit.CheckAndIncrementAsync(key, maxCount: 3, windowSeconds: 60))
                .Should().BeTrue($"call {i} is within the limit");
        }
        (await rateLimit.CheckAndIncrementAsync(key, maxCount: 3, windowSeconds: 60))
            .Should().BeFalse("the call past the limit is blocked");
    }
}
