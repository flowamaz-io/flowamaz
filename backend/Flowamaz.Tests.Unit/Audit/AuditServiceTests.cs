using FluentAssertions;
using Flowamaz.Api.Controllers;
using Flowamaz.Core.Entities.Audit;
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

    private sealed class FakeRequestContext(string? ip, string? userAgent) : IRequestContextAccessor
    {
        public string? GetIpAddress() => ip;
        public string? GetUserAgent() => userAgent;
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

        await Task.Delay(1500);

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var stored = await verifyDb.AuditEvents.AsNoTracking().SingleAsync(e => e.OrgId == orgId);

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
}
