using FluentAssertions;
using Flowamaz.Application.Platform.Services;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Public;

/// <summary>Per-minute public API limit by plan (FUNCTIONAL.md §13.2), with the organisation's own figure winning.</summary>
public class PublicApiPlanLimitsTests
{
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IOrganisationRepository> _orgs = new();
    private readonly Mock<IPlanRepository> _plans = new();
    private readonly Guid _ws = Guid.NewGuid();
    private readonly Organisation _org = new() { Id = Guid.NewGuid(), PlanId = Guid.NewGuid() };

    private PublicApiPlanLimits Service(PublicApiPlanLimitCache? cache = null)
    {
        _workspaces.Setup(r => r.GetByIdAsync(_ws, It.IsAny<CancellationToken>())).ReturnsAsync(new Workspace { Id = _ws, OrgId = _org.Id });
        _orgs.Setup(r => r.GetByIdAsync(_org.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_org);
        return new PublicApiPlanLimits(_workspaces.Object, _orgs.Object, _plans.Object, cache ?? new PublicApiPlanLimitCache(),
            NullLogger<PublicApiPlanLimits>.Instance);
    }

    private void OnPlan(string name, int perMinute) =>
        _plans.Setup(r => r.GetByIdAsync(_org.PlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Plan { Id = _org.PlanId, Name = name, Limits = new PlanLimits { ApiRateLimitPerMinute = perMinute } });

    [Theory]
    [InlineData("Community", 10)]
    [InlineData("Starter", 60)]
    [InlineData("Pro", 300)]
    public async Task Uses_the_plan_limit(string plan, int perMinute)
    {
        OnPlan(plan, perMinute);
        var limit = await Service().ResolveAsync(_ws);
        limit.Should().Be(new Core.Interfaces.Services.PlanApiLimit(_org.Id, plan, perMinute));
    }

    [Fact]
    public async Task Enterprise_without_an_agreed_figure_has_no_per_minute_limit()
    {
        OnPlan("Enterprise", 0);
        (await Service().ResolveAsync(_ws))!.PerMinute.Should().Be(0);
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(0)]
    public async Task The_organisations_own_figure_wins(int custom)
    {
        OnPlan("Enterprise", 0);
        _org.ApiRateLimitPerMinute = custom;
        (await Service().ResolveAsync(_ws))!.PerMinute.Should().Be(custom);
    }

    [Fact]
    public async Task Unknown_workspace_resolves_to_null()
    {
        var limit = await Service().ResolveAsync(Guid.NewGuid());
        limit.Should().BeNull();
    }

    [Fact]
    public async Task Results_are_cached_for_a_minute()
    {
        OnPlan("Starter", 60);
        var cache = new PublicApiPlanLimitCache();
        var service = Service(cache);

        await service.ResolveAsync(_ws);
        await service.ResolveAsync(_ws);

        _plans.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
