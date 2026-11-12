using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Auth.Scim;
using Flowamaz.Core.Entities.Audit;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Jobs;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Seed;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Quartz;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// The gaps closed together: current access tokens end with the session (SCIM deactivation, role changes, unlinking an
/// SSO identity), large SCIM group changes re-apply roles in the background, two admins can't demote each other at the
/// same moment, Flowamaz staff set an agreed API limit (owners only see it), and audit events outlive a plan's history.
/// </summary>
[Collection("api")]
public sealed class SessionAndAccessGapTests : ApiTestBase
{
    public SessionAndAccessGapTests(IntegrationApiFixture fixture) : base(fixture) { }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>A slug unique to this run (the test database is kept between runs).</summary>
    private static string U(string slug) => $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

    private async Task<T> DbAsync<T>(Func<FlowAmazDbContext, Task<T>> query)
    {
        using var scope = Fixture.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>());
    }

    private Task SetPlanAsync(Guid orgId, Guid planId) =>
        DbAsync(db => db.Organisations.Where(o => o.Id == orgId).ExecuteUpdateAsync(s => s.SetProperty(o => o.PlanId, planId)));

    /// <summary>A password-less person in the organisation, optionally a member of a workspace.</summary>
    private async Task<Guid> AddPersonAsync(Guid orgId, string email, Guid? workspaceId = null, WorkspaceRole role = WorkspaceRole.Viewer)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var user = new OrgUser { OrgId = orgId, Email = email, Name = email.Split('@')[0], PasswordHash = string.Empty, IsActive = true };
        db.OrgUsers.Add(user);
        if (workspaceId is { } ws) db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = ws, OrgUserId = user.Id, Role = role, IsActive = true });
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>An access token for the person as a sign-in would issue it now (current roles in the claims).</summary>
    private async Task<HttpClient> SignedInAsync(Guid userId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var user = await db.OrgUsers.AsNoTracking().SingleAsync(u => u.Id == userId);
        var slug = await db.Organisations.Where(o => o.Id == user.OrgId).Select(o => o.Slug).SingleAsync();
        var memberships = await scope.ServiceProvider.GetRequiredService<IWorkspaceMemberRepository>().GetActiveMembershipsForUserAsync(userId);
        var token = scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateAccessToken(user, slug, memberships);
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpStatusCode> StatusAsync(HttpClient client, string url) =>
        client.GetAsync(url).ContinueWith(t => t.Result.StatusCode);

    private static StringContent Scim(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/scim+json");

    private async Task<HttpClient> ScimClientAsync(Owner owner)
    {
        var created = await owner.Client.PostAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/scim/tokens", new { name = "Okta" });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await DataAsync(created)).GetProperty("plainToken").GetString());
        return client;
    }

    // ── Access tokens end with the session ─────────────────────────────────────────────

    [Fact]
    public async Task Deactivating_a_person_over_scim_ends_their_current_access_token()
    {
        var owner = await RegisterOwnerAsync(U("gap-scim-cut"));
        await SetPlanAsync(owner.OrgId, PlatformSeedData.EnterprisePlanId);
        var scim = await ScimClientAsync(owner);
        var userId = await AddPersonAsync(owner.OrgId, "ana@gap-scim-cut.test");
        var ana = await SignedInAsync(userId);
        (await StatusAsync(ana, "/api/v1/workspaces")).Should().Be(HttpStatusCode.OK);

        var off = await scim.PatchAsync($"/api/scim/v2/Users/{userId}", Scim(new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" },
            Operations = new[] { new { op = "replace", value = new { active = false } } },
        }));
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());

        (await StatusAsync(ana, "/api/v1/workspaces")).Should().Be(HttpStatusCode.Unauthorized,
            "the token was issued before the deactivation, so it no longer works even though it hasn't expired");
        (await StatusAsync(owner.Client, "/api/v1/workspaces")).Should().Be(HttpStatusCode.OK, "nobody else is affected");
    }

    [Fact]
    public async Task A_role_change_applies_at_once_and_a_fresh_token_carries_the_new_role()
    {
        var owner = await RegisterOwnerAsync(U("gap-role-cut"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Lending", "gap-role-cut-ws");
        var benId = await AddPersonAsync(owner.OrgId, "ben@gap-role-cut.test", ws, WorkspaceRole.Admin);
        var ben = await SignedInAsync(benId);
        (await StatusAsync(ben, $"/api/v1/workspaces/{ws}/members")).Should().Be(HttpStatusCode.OK);
        (await DataAsync(await owner.Client.GetAsync("/api/v1/billing/usage"))).GetProperty("membersUsed").GetInt32()
            .Should().Be(2, "billing usage counts the largest workspace's active members (the owner and Ben)");

        var demote = await owner.Client.PatchAsJsonAsync($"/api/v1/workspaces/{ws}/members/{benId}/role", new { role = "Viewer" });
        demote.StatusCode.Should().Be(HttpStatusCode.NoContent, await demote.Content.ReadAsStringAsync());

        (await StatusAsync(ben, $"/api/v1/workspaces/{ws}/members")).Should().Be(HttpStatusCode.Unauthorized,
            "the old token still says Admin, so it's refused (the browser then refreshes it)");
        var refreshed = await SignedInAsync(benId);
        var promote = await refreshed.PatchAsJsonAsync($"/api/v1/workspaces/{ws}/members/{benId}/role", new { role = "Admin" });
        promote.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the fresh token carries the Viewer role");
    }

    [Fact]
    public async Task Two_admins_demoting_each_other_at_once_leave_the_workspace_with_an_admin()
    {
        var owner = await RegisterOwnerAsync(U("gap-admin-race"));
        var ownerId = await DbAsync(db => db.OrgUsers.Where(u => u.OrgId == owner.OrgId).Select(u => u.Id).SingleAsync());
        for (var round = 0; round < 5; round++)
        {
            // A demotion ends the demoted admin's current token, so sign in afresh each round (as the browser would).
            var ws = await CreateWorkspaceAsync(await SignedInAsync(ownerId), $"Race {round}", $"gap-admin-race-{round}");
            var otherId = await AddPersonAsync(owner.OrgId, $"admin{round}@gap-admin-race.test", ws, WorkspaceRole.Admin);
            var other = await SignedInAsync(otherId);
            var ownerNow = await SignedInAsync(ownerId); // includes the new workspace

            var results = await Task.WhenAll(
                ownerNow.PatchAsJsonAsync($"/api/v1/workspaces/{ws}/members/{otherId}/role", new { role = "Viewer" }),
                other.PatchAsJsonAsync($"/api/v1/workspaces/{ws}/members/{ownerId}/role", new { role = "Viewer" }));

            results.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1, $"round {round}: only one demotion can win");
            // The loser is refused: as the last Admin (409) if it got the lock second, or — if the winner's demotion of
            // it landed first — because its token was just ended (401) or its role is already Viewer (403).
            results.Count(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                .Should().Be(1, $"round {round}: the other demotion is refused");
            var admins = await DbAsync(db => db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == ws && m.IsActive && m.Role == WorkspaceRole.Admin));
            admins.Should().Be(1);
        }
    }

    [Fact]
    public async Task Signing_out_ends_that_access_token_but_not_the_persons_other_sessions()
    {
        var owner = await RegisterOwnerAsync(U("gap-logout"));
        var userId = await AddPersonAsync(owner.OrgId, "ana@gap-logout.test");
        var laptop = await SignedInAsync(userId);
        var phone = await SignedInAsync(userId);

        (await laptop.PostAsJsonAsync("/api/v1/auth/logout", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await StatusAsync(laptop, "/api/v1/workspaces")).Should().Be(HttpStatusCode.Unauthorized, "the signed-out token stops at once");
        (await StatusAsync(phone, "/api/v1/workspaces")).Should().Be(HttpStatusCode.OK, "her other session is untouched");
    }

    [Fact]
    public async Task Markers_and_issue_times_share_one_clock_so_servers_with_wrong_clocks_still_agree()
    {
        var redis = Fixture.Services.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();
        var options = Microsoft.Extensions.Options.Options.Create(new Flowamaz.Core.Configuration.JwtOptions { AccessTokenExpiryMinutes = 15 });
        long Real() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ahead = new Flowamaz.Infrastructure.Services.RedisTokenRevocationService(redis, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Flowamaz.Infrastructure.Services.RedisTokenRevocationService>.Instance, () => Real() + 300_000);
        var behind = new Flowamaz.Infrastructure.Services.RedisTokenRevocationService(redis, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Flowamaz.Infrastructure.Services.RedisTokenRevocationService>.Instance, () => Real() - 300_000);
        var user = Guid.NewGuid();

        var oldToken = behind.IssueTimeUnixMs();      // issued by the slow server before the change
        await Task.Delay(5);
        await ahead.RevokeUserAccessAsync(user);      // the change is recorded by the fast server
        await Task.Delay(5);
        var newToken = behind.IssueTimeUnixMs();      // the slow server issues a fresh token afterwards

        (await behind.IsRevokedAsync(user, Guid.NewGuid(), false, oldToken)).Should().BeTrue();
        (await ahead.IsRevokedAsync(user, Guid.NewGuid(), false, newToken)).Should().BeFalse(
            "five minutes of clock difference would otherwise make the fresh token look older than the change");
        (await ahead.IsRevokedAsync(user, Guid.NewGuid(), false, ahead.IssueTimeUnixMs())).Should().BeFalse(
            "a token the marking server issues right after its own marker is always newer");
    }

    // ── SSO identity unlink ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_owner_unlinks_an_sso_identity_and_the_person_is_signed_out()
    {
        var owner = await RegisterOwnerAsync(U("gap-unlink"));
        var anaId = await AddPersonAsync(owner.OrgId, "ana@gap-unlink.test");
        await DbAsync(async db =>
        {
            await db.OrgUsers.Where(u => u.Id == anaId).ExecuteUpdateAsync(s => s.SetProperty(u => u.SsoSubject, "idp|old"));
            db.RefreshTokens.Add(new Core.Entities.Auth.RefreshToken { OrgUserId = anaId, TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = DateTime.UtcNow.AddDays(1) });
            return await db.SaveChangesAsync();
        });
        var ana = await SignedInAsync(anaId);

        var list = await DataAsync(await owner.Client.GetAsync($"/api/v1/organisations/{owner.OrgId}/sso/identities?search=ANA@"));
        list.GetProperty("total").GetInt32().Should().Be(1);
        list.GetProperty("items")[0].GetProperty("email").GetString().Should().Be("ana@gap-unlink.test");
        list.ToString().Should().NotContain("idp|old", "the IdP subject itself is never shown");

        (await owner.Client.DeleteAsync($"/api/v1/organisations/{owner.OrgId}/sso/identities/{anaId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DbAsync(db => db.OrgUsers.Where(u => u.Id == anaId).Select(u => u.SsoSubject).SingleAsync())).Should().BeNull();
        (await DbAsync(db => db.RefreshTokens.AnyAsync(t => t.OrgUserId == anaId && t.RevokedAt == null))).Should().BeFalse();
        (await StatusAsync(ana, "/api/v1/workspaces")).Should().Be(HttpStatusCode.Unauthorized);
        (await DataAsync(await owner.Client.GetAsync($"/api/v1/organisations/{owner.OrgId}/sso/identities"))).GetProperty("total").GetInt32().Should().Be(0);

        (await (await SignedInAsync(anaId)).GetAsync($"/api/v1/organisations/{owner.OrgId}/sso/identities"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "only the owner sees linked sign-ins");

        // Someone else's organisation, or someone else's person: not found.
        var other = await RegisterOwnerAsync(U("gap-unlink-other"));
        (await other.Client.DeleteAsync($"/api/v1/organisations/{other.OrgId}/sso/identities/{anaId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.Client.GetAsync($"/api/v1/organisations/{owner.OrgId}/sso/identities")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Large SCIM groups ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_large_scim_group_change_re_applies_roles_in_the_background()
    {
        var owner = await RegisterOwnerAsync(U("gap-big-group"));
        await SetPlanAsync(owner.OrgId, PlatformSeedData.EnterprisePlanId);
        var ws = await CreateWorkspaceAsync(owner.Client, "Lending", "gap-big-group-ws");
        var sso = await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "oidc", isActive = false, issuerUrl = "https://idp.acme.test", clientId = "flowamaz",
            groupRoleMappings = new[] { new { group = "Everyone", workspaceId = ws, role = "Runner" } },
            rolesFromScim = true,
        });
        sso.StatusCode.Should().Be(HttpStatusCode.OK, await sso.Content.ReadAsStringAsync());
        var scim = await ScimClientAsync(owner);

        var count = ScimService.InlineResyncLimit + 20;
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++) ids.Add(await AddPersonAsync(owner.OrgId, $"p{i}@gap-big-group.test"));
        var someone = await SignedInAsync(ids[0]);

        var create = await scim.PostAsync("/api/scim/v2/Groups", Scim(new { displayName = "Everyone", members = ids.Select(id => new { value = id }).ToArray() }));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        (await DbAsync(db => db.ScimGroupMembers.CountAsync(m => ids.Contains(m.OrgUserId)))).Should().Be(count, "the membership is saved at once");
        (await DbAsync(db => db.ScimRoleResyncs.CountAsync(r => r.OrgId == owner.OrgId))).Should().Be(count, "the roles wait for the job");
        (await DbAsync(db => db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == ws && ids.Contains(m.OrgUserId)))).Should().Be(0);

        // What the Quartz job does every 10 seconds.
        using (var scope = Fixture.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ScimService>();
            (await service.ProcessQueuedRoleResyncsAsync(100)).Should().Be(100);
            (await service.ProcessQueuedRoleResyncsAsync(100)).Should().Be(20);
            (await service.ProcessQueuedRoleResyncsAsync(100)).Should().Be(0);
        }
        (await DbAsync(db => db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == ws && ids.Contains(m.OrgUserId) && m.IsActive && m.Role == WorkspaceRole.Runner)))
            .Should().Be(count);
        (await DbAsync(db => db.ScimRoleResyncs.CountAsync(r => r.OrgId == owner.OrgId))).Should().Be(0);
        (await StatusAsync(someone, "/api/v1/workspaces")).Should().Be(HttpStatusCode.Unauthorized,
            "a token from before the new roles is refreshed to carry them");
    }

    [Fact]
    public async Task A_person_queued_again_while_the_job_runs_stays_queued_for_the_newer_change()
    {
        var owner = await RegisterOwnerAsync(U("gap-requeue"));
        var person = await AddPersonAsync(owner.OrgId, "p@gap-requeue.test");
        using (var scope = Fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IScimRepository>().QueueRoleResyncAsync(owner.OrgId, [person]);

        // The job claims the row (and holds its lock) …
        using var jobScope = Fixture.Services.CreateScope();
        var jobDb = jobScope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var jobRepo = jobScope.ServiceProvider.GetRequiredService<IScimRepository>();
        await using var claim = await jobDb.Database.BeginTransactionAsync();
        var claimed = (await jobRepo.ClaimRoleResyncsAsync(1000, ScimService.MaxResyncAttempts)).Where(r => r.OrgUserId == person).ToList();
        claimed.Should().HaveCount(1);

        // … while a second group change queues the same person: it waits for the job instead of being dropped.
        using var requestScope = Fixture.Services.CreateScope();
        var requeue = requestScope.ServiceProvider.GetRequiredService<IScimRepository>().QueueRoleResyncAsync(owner.OrgId, [person]);
        await Task.Delay(300);
        requeue.IsCompleted.Should().BeFalse("the queued row is locked by the job");

        await jobRepo.DeleteRoleResyncsAsync(claimed);
        await claim.CommitAsync();
        await requeue;

        (await DbAsync(db => db.ScimRoleResyncs.CountAsync(r => r.OrgUserId == person))).Should().Be(1,
            "the newer change is still waiting to be applied");
        await DbAsync(db => db.ScimRoleResyncs.Where(r => r.OrgUserId == person).ExecuteDeleteAsync()); // leave the queue empty
    }

    // ── Agreed API limits ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flowamaz_staff_set_an_agreed_api_limit_and_the_owner_sees_it_but_cannot_change_it()
    {
        var customer = await RegisterOwnerAsync(U("gap-api-limit"));
        await SetPlanAsync(customer.OrgId, PlatformSeedData.EnterprisePlanId);
        var staff = await RegisterOwnerAsync(U("gap-api-staff"));
        var config = Fixture.Services.GetRequiredService<IConfiguration>();
        (await customer.Client.PutAsJsonAsync($"/api/v1/platform/organisations/{customer.OrgId}/api-limit", new { perMinute = 99999 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "an owner can't raise their own limit");
        (await staff.Client.GetAsync("/api/v1/platform/organisations")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "not staff until configured");

        config["Platform:StaffOrgIds"] = staff.OrgId.ToString();
        try
        {
            var me = await DataAsync(await staff.Client.GetAsync("/api/v1/auth/me"));
            me.GetProperty("isPlatformStaff").GetBoolean().Should().BeTrue();

            var found = await DataAsync(await staff.Client.GetAsync($"/api/v1/platform/organisations?search={customer.OrgSlug}"));
            found.GetArrayLength().Should().Be(1);
            found[0].GetProperty("planPerMinute").GetInt32().Should().Be(1200);
            found[0].GetProperty("customPerMinute").ValueKind.Should().Be(JsonValueKind.Null);

            (await staff.Client.PutAsJsonAsync($"/api/v1/platform/organisations/{customer.OrgId}/api-limit", new { perMinute = -1 }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var set = await staff.Client.PutAsJsonAsync($"/api/v1/platform/organisations/{customer.OrgId}/api-limit", new { perMinute = 5000 });
            set.StatusCode.Should().Be(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
            (await DataAsync(set)).GetProperty("effectivePerMinute").GetInt32().Should().Be(5000);

            var billing = await DataAsync(await customer.Client.GetAsync("/api/v1/billing/current"));
            billing.GetProperty("apiCallsPerMinute").GetInt32().Should().Be(5000);
            billing.GetProperty("apiLimitIsCustom").GetBoolean().Should().BeTrue();

            var back = await staff.Client.PutAsJsonAsync($"/api/v1/platform/organisations/{customer.OrgId}/api-limit", new { perMinute = (int?)null });
            (await DataAsync(back)).GetProperty("effectivePerMinute").GetInt32().Should().Be(1200);
            (await staff.Client.PutAsJsonAsync($"/api/v1/platform/organisations/{Guid.NewGuid()}/api-limit", new { perMinute = 10 }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            config["Platform:StaffOrgIds"] = null;
        }
    }

    // ── Audit retention ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Audit_events_past_the_plans_history_are_kept_until_the_platform_retention()
    {
        var owner = await RegisterOwnerAsync(U("gap-retention"));
        Guid KeepId = Guid.NewGuid(), DropId = Guid.NewGuid();
        await DbAsync(db =>
        {
            db.AuditEvents.Add(new AuditEvent { Id = KeepId, OrgId = owner.OrgId, EventType = "test.kept", ResourceType = "test", Action = "created", ActorType = "system", CreatedAt = DateTime.UtcNow.AddDays(-400) });
            db.AuditEvents.Add(new AuditEvent { Id = DropId, OrgId = owner.OrgId, EventType = "test.dropped", ResourceType = "test", Action = "created", ActorType = "system", CreatedAt = DateTime.UtcNow.AddDays(-(AuditRetentionJob.DefaultRetentionDays + 5)) });
            return db.SaveChangesAsync();
        });

        using (var scope = Fixture.Services.CreateScope())
        {
            var job = ActivatorUtilities.CreateInstance<AuditRetentionJob>(scope.ServiceProvider);
            var context = new Mock<IJobExecutionContext>();
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            await job.Execute(context.Object);
        }

        var left = await DbAsync(db => db.AuditEvents.Where(e => e.OrgId == owner.OrgId && e.ResourceType == "test").Select(e => e.Id).ToListAsync());
        left.Should().Contain(KeepId, "a Community organisation's 400-day-old event is hidden from its log, not deleted");
        left.Should().NotContain(DropId, "events older than the platform retention (7 years) are deleted");
    }
}
