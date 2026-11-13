using FluentAssertions;
using Flowamaz.Api.Controllers;
using Flowamaz.Core.Entities.Audit;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Audit;

public class AuditServiceTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeRequestContext(string? ip, string? userAgent, string? apiKeyLabel = null) : IRequestContextAccessor
    {
        public string? GetIpAddress() => ip;
        public string? GetUserAgent() => userAgent;
        public string? GetApiKeyAuditLabel() => apiKeyLabel;
    }

    // The root must be kept alive for the test's duration — the InMemory provider holds it weakly,
    // so if it is GC'd between the background write and the read the store is dropped — the test
    // keeps the root referenced for the whole method via a local.

    // ── 1. RecordAsync stores the event with the correct fields ──────────────

    [Fact]
    public async Task RecordAsync_stores_event_with_correct_fields()
    {
        var root = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<FlowAmazDbContext>(o => o.UseInMemoryDatabase("store-fields", root));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var service = new AuditService(scopeFactory, new FakeRequestContext("203.0.113.7", "vitest"), NullLogger<AuditService>.Instance);

        var orgId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        service.RecordAsync(new AuditEventRequest
        {
            OrgId = orgId,
            WorkspaceId = workspaceId,
            ActorUserId = actorId,
            ActorType = "user",
            ActorLabel = "Jagan",
            EventType = "workflow.created",
            ResourceType = "workflow",
            ResourceId = resourceId,
            ResourceLabel = "Fund Pipeline",
            Action = "created",
            Metadata = new { foo = "bar" },
        });

        var stored = await WaitForEventAsync(scopeFactory, orgId);

        stored.WorkspaceId.Should().Be(workspaceId);
        stored.ActorUserId.Should().Be(actorId);
        stored.ActorType.Should().Be("user");
        stored.ActorLabel.Should().Be("Jagan");
        stored.EventType.Should().Be("workflow.created");
        stored.ResourceType.Should().Be("workflow");
        stored.ResourceId.Should().Be(resourceId);
        stored.ResourceLabel.Should().Be("Fund Pipeline");
        stored.Action.Should().Be("created");
        stored.IpAddress.Should().Be("203.0.113.7");
        stored.UserAgent.Should().Be("vitest");
        stored.Metadata.Should().Contain("bar");
        GC.KeepAlive(root);
    }

    // ── 1b. O4: API-key requests are attributed to the key ───────────────────

    [Fact]
    public async Task RecordAsync_attributes_actions_without_a_user_to_the_api_key_of_the_request()
    {
        var root = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<FlowAmazDbContext>(o => o.UseInMemoryDatabase("api-key-actor", root));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        const string label = "API key \"Loan app\" (fmz_live_loans_…)";
        var service = new AuditService(scopeFactory, new FakeRequestContext("203.0.113.7", "curl", label), NullLogger<AuditService>.Instance);

        var orgId = Guid.NewGuid();
        service.RecordAsync(new AuditEventRequest
        {
            OrgId = orgId,
            ActorUserId = Guid.Empty, // public API gate decision passes no human
            ActorType = "system",
            EventType = "gate.approved",
            ResourceType = "gate",
            Action = "approved",
        });

        var stored = await WaitForEventAsync(scopeFactory, orgId);
        stored.ActorType.Should().Be("api_key");
        stored.ActorLabel.Should().Be(label);
        stored.ActorUserId.Should().BeNull();
        GC.KeepAlive(root);
    }

    [Fact]
    public async Task RecordAsync_keeps_a_caller_named_actor_and_adds_the_api_key()
    {
        // Public-API gate decision with decided_by: the person's name, then the key that sent it.
        var root = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<FlowAmazDbContext>(o => o.UseInMemoryDatabase("api-key-named-actor", root));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        const string keyLabel = "API key \"Loan app\" (fmz_live_loans_…)";
        var service = new AuditService(scopeFactory, new FakeRequestContext("203.0.113.7", "curl", keyLabel), NullLogger<AuditService>.Instance);

        var orgId = Guid.NewGuid();
        service.RecordAsync(new AuditEventRequest
        {
            OrgId = orgId,
            ActorType = "system",
            ActorLabel = "Aisha Rahman (Branch Manager)",
            EventType = "gate.approved",
            ResourceType = "gate",
            Action = "approved",
        });

        var stored = await WaitForEventAsync(scopeFactory, orgId);
        stored.ActorType.Should().Be("api_key");
        stored.ActorLabel.Should().Be($"Aisha Rahman (Branch Manager) via {keyLabel}");
        GC.KeepAlive(root);
    }

    [Fact]
    public async Task RecordAsync_truncates_an_actor_label_longer_than_the_column()
    {
        var root = new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<FlowAmazDbContext>(o => o.UseInMemoryDatabase("long-actor-label", root));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var service = new AuditService(scopeFactory, new FakeRequestContext(null, null, new string('k', 120)), NullLogger<AuditService>.Instance);

        var orgId = Guid.NewGuid();
        service.RecordAsync(new AuditEventRequest
        {
            OrgId = orgId,
            ActorType = "system",
            ActorLabel = new string('n', 200),
            EventType = "gate.rejected",
            ResourceType = "gate",
            Action = "rejected",
        });

        var stored = await WaitForEventAsync(scopeFactory, orgId);
        stored.ActorLabel!.Length.Should().Be(256);
        stored.ActorLabel.Should().EndWith("…");
        GC.KeepAlive(root);
    }

    [Fact]
    public void ApiKey_audit_label_uses_name_and_display_prefix_only()
    {
        var result = new WorkspaceApiKeyValidationResult(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [], "Loan app", "fmz_live_loans_");
        result.AuditLabel.Should().Be("API key \"Loan app\" (fmz_live_loans_…)");
    }

    // ── 2. Error in storage does not propagate (fire-and-forget) ─────────────

    [Fact]
    public async Task RecordAsync_when_db_write_fails_does_not_throw_on_caller_thread()
    {
        var services = new ServiceCollection();
        services.AddDbContext<FlowAmazDbContext>(o => o.UseInMemoryDatabase($"audit-fail-{Guid.NewGuid():N}"));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var service = new AuditService(scopeFactory, new FakeRequestContext(null, null), NullLogger<AuditService>.Instance);
        await sp.DisposeAsync(); // provider gone — the background write will throw and must be swallowed

        var act = () => service.RecordAsync(new AuditEventRequest
        {
            OrgId = Guid.NewGuid(),
            EventType = "workflow.created",
            ResourceType = "workflow",
            Action = "created",
        });

        act.Should().NotThrow("audit errors are logged on the background task, never propagated");
        await Task.Delay(100); // let the background task run and swallow its exception
    }

    // ── 3. IP extracted correctly from X-Forwarded-For ───────────────────────

    [Fact]
    public void HttpRequestContextAccessor_prefers_first_X_Forwarded_For_hop()
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Headers["X-Forwarded-For"] = "198.51.100.10, 10.0.0.1, 10.0.0.2";
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.99");
        var accessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(http);

        var sut = new Flowamaz.Api.Identity.HttpRequestContextAccessor(accessor.Object);

        sut.GetIpAddress().Should().Be("198.51.100.10",
            "behind nginx the client IP is the first hop of X-Forwarded-For, not the socket address");
    }

    [Fact]
    public void HttpRequestContextAccessor_falls_back_to_socket_when_no_forwarded_header()
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.5");
        var accessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(http);

        var sut = new Flowamaz.Api.Identity.HttpRequestContextAccessor(accessor.Object);

        sut.GetIpAddress().Should().Be("192.0.2.5");
    }

    // ── 4. AuditController: org-scope mismatch → 403 (InsufficientRoleException) ─

    [Fact]
    public async Task AuditController_ListForOrg_other_org_throws_403()
    {
        var callerOrg = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        var controller = BuildController(callerOrg);

        var act = () => controller.ListForOrg(otherOrg, new AuditQueryParams(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InsufficientRoleException>();
        ex.Which.HttpStatusCode.Should().Be(403);
    }

    // ── 5. AuditController: date range > 90 days → 400 (AuditException) ───────

    [Fact]
    public async Task AuditController_ListForWorkspace_range_over_90_days_throws_400()
    {
        var orgId = Guid.NewGuid();
        var controller = BuildController(orgId);
        var to = DateTime.UtcNow;
        var from = to.AddDays(-120); // > 90 day cap

        var act = () => controller.ListForWorkspace(
            Guid.NewGuid(), new AuditQueryParams { From = from, To = to }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AuditException>();
        ex.Which.HttpStatusCode.Should().Be(400);
        ex.Which.ErrorCode.Should().Be("AUDIT_RANGE_TOO_WIDE");
    }

    // ── 6. Plan: history days and export ─────────────────────────────────────────

    private static (AuditController Controller, List<AuditQueryFilter> Queries) ControllerOnPlan(
        Guid orgId, int historyDays, bool canExport)
    {
        var queries = new List<AuditQueryFilter>();
        var repo = new Mock<IAuditEventRepository>();
        repo.Setup(r => r.QueryAsync(It.IsAny<AuditQueryFilter>(), It.IsAny<CancellationToken>()))
            .Callback<AuditQueryFilter, CancellationToken>((f, _) => queries.Add(f))
            .ReturnsAsync(((IReadOnlyList<AuditEvent>)Array.Empty<AuditEvent>(), 0));
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.OrgId).Returns(orgId);
        var planId = Guid.NewGuid();
        var orgs = new Mock<IOrganisationRepository>();
        orgs.Setup(o => o.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(new Organisation { Id = orgId, PlanId = planId });
        var plans = new Mock<IPlanRepository>();
        plans.Setup(p => p.GetByIdAsync(planId, It.IsAny<CancellationToken>())).ReturnsAsync(new Plan
        {
            Id = planId, Name = canExport ? "Starter" : "Community",
            Limits = new Core.Models.PlanLimits { AuditHistoryDays = historyDays },
            Features = new Core.Models.PlanFeatures { AuditLog = true, AuditExport = canExport },
        });
        var controller = new AuditController(repo.Object, new Mock<IWorkspaceRepository>().Object, currentUser.Object,
            NullLogger<AuditController>.Instance, orgs.Object, plans.Object);
        return (controller, queries);
    }

    [Fact]
    public async Task The_audit_log_only_reaches_back_as_far_as_the_plan_allows()
    {
        var orgId = Guid.NewGuid();
        var (controller, queries) = ControllerOnPlan(orgId, historyDays: 7, canExport: false);

        var result = await controller.ListForWorkspace(Guid.NewGuid(),
            new AuditQueryParams { From = DateTime.UtcNow.AddDays(-30), To = DateTime.UtcNow }, CancellationToken.None);

        queries.Single().From.Should().BeCloseTo(DateTime.UtcNow.AddDays(-7), TimeSpan.FromMinutes(1),
            "older events are kept but not shown on a 7-day plan");
        var page = (AuditPageResponse)((Microsoft.AspNetCore.Mvc.OkObjectResult)result.Result!).Value!;
        page.History!.Days.Should().Be(7);
        page.History.CanExport.Should().BeFalse();
        page.History.StartsAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Full_history_plans_are_not_cut_off()
    {
        var orgId = Guid.NewGuid();
        var (controller, queries) = ControllerOnPlan(orgId, historyDays: 0, canExport: true);
        var from = DateTime.UtcNow.AddDays(-60);

        await controller.ListForWorkspace(Guid.NewGuid(), new AuditQueryParams { From = from, To = DateTime.UtcNow }, CancellationToken.None);

        queries.Single().From.Should().Be(from);
    }

    [Fact]
    public async Task Organisation_wide_audit_needs_a_plan_with_export()
    {
        var orgId = Guid.NewGuid();
        var (controller, _) = ControllerOnPlan(orgId, historyDays: 7, canExport: false);

        var act = () => controller.ListForOrg(orgId, new AuditQueryParams(), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<AuditException>()).Which;
        ex.HttpStatusCode.Should().Be(403);
        ex.ErrorCode.Should().Be("AUDIT_EXPORT_NOT_ON_PLAN");
        ex.Message.Should().Contain("Community").And.Contain("Settings → Billing");
    }

    private static AuditController BuildController(Guid callerOrgId)
    {
        var repo = new Mock<IAuditEventRepository>();
        repo.Setup(r => r.QueryAsync(It.IsAny<AuditQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<AuditEvent>)Array.Empty<AuditEvent>(), 0));
        var workspaces = new Mock<IWorkspaceRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.OrgId).Returns(callerOrgId);
        return new AuditController(repo.Object, workspaces.Object, currentUser.Object, NullLogger<AuditController>.Instance);
    }

    /// <summary>
    /// RecordAsync writes in the background; wait for the row (up to 10 s) instead of a fixed delay, which
    /// failed on a slow CI runner.
    /// </summary>
    private static async Task<Flowamaz.Core.Entities.Audit.AuditEvent> WaitForEventAsync(IServiceScopeFactory scopeFactory, Guid orgId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var stored = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(e => e.OrgId == orgId);
            if (stored is not null) return stored;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The audit event was not written within 10 seconds.");
            await Task.Delay(50);
        }
    }
}
