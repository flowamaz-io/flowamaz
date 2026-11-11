using System.Net;
using FluentAssertions;
using Flowamaz.Core.Entities.Audit;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Seed;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// Plans from 11 Nov 2026: Community has the API and a 7-day audit history; export and the organisation-wide
/// view are on Starter and above; older events are kept and come back on upgrading.
/// </summary>
[Collection("api")]
public sealed class PlanAuditHistoryTests : ApiTestBase
{
    public PlanAuditHistoryTests(IntegrationApiFixture fixture) : base(fixture) { }

    private async Task AddEventAsync(Guid orgId, Guid workspaceId, string type, int daysAgo)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        db.AuditEvents.Add(new AuditEvent
        {
            OrgId = orgId, WorkspaceId = workspaceId, EventType = type, ResourceType = "workflow", Action = "created",
            ResourceLabel = type, CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Community_sees_seven_days_and_no_export_until_it_upgrades()
    {
        var owner = await RegisterOwnerAsync("plan-audit");
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", "ops");
        await AddEventAsync(owner.OrgId, ws, "test.recent", daysAgo: 2);
        await AddEventAsync(owner.OrgId, ws, "test.month_old", daysAgo: 30);
        var from = DateTime.UtcNow.AddDays(-60).ToString("O");
        var url = $"/api/v1/workspaces/{ws}/audit?from={Uri.EscapeDataString(from)}";

        var community = await DataAsync(await owner.Client.GetAsync(url));
        var types = community.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("eventType").GetString()).ToList();
        types.Should().Contain("test.recent").And.NotContain("test.month_old");
        community.GetProperty("history").GetProperty("days").GetInt32().Should().Be(7);
        community.GetProperty("history").GetProperty("canExport").GetBoolean().Should().BeFalse();

        (await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/audit/export")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.Client.GetAsync($"/api/v1/organisations/{owner.OrgId}/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            await db.Organisations.Where(o => o.Id == owner.OrgId).ExecuteUpdateAsync(s => s.SetProperty(o => o.PlanId, PlatformSeedData.StarterPlanId));
        }

        var starter = await DataAsync(await owner.Client.GetAsync(url));
        starter.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("eventType").GetString())
            .Should().Contain("test.month_old", "the event was kept and shows again within Starter's 90 days");
        (await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/audit/export")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.Client.GetAsync($"/api/v1/organisations/{owner.OrgId}/audit")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_plans_list_shows_the_new_limits_and_audit_history()
    {
        var owner = await RegisterOwnerAsync("plan-list");
        var plans = await DataAsync(await owner.Client.GetAsync("/api/v1/billing/plans"));
        var bySlug = plans.EnumerateArray().ToDictionary(p => p.GetProperty("slug").GetString()!);

        bySlug["community"].GetProperty("apiCallsPerMinute").GetInt32().Should().Be(30);
        bySlug["starter"].GetProperty("apiCallsPerMinute").GetInt32().Should().Be(120);
        bySlug["pro"].GetProperty("apiCallsPerMinute").GetInt32().Should().Be(600);
        var community = bySlug["community"].GetProperty("features").EnumerateArray().Select(f => f.GetString()).ToList();
        community.Should().Contain("API access").And.Contain("Audit log (7 days of history)").And.NotContain(f => f!.StartsWith("Audit export"));
        bySlug["pro"].GetProperty("features").EnumerateArray().Select(f => f.GetString())
            .Should().Contain("Audit log (1 year of history)").And.Contain("Audit export and organisation-wide audit");
    }
}
