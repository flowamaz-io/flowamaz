using FluentAssertions;
using Flowamaz.Application.Platform.Services;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Flowamaz.Tests.Unit.Platform;

/// <summary>
/// EditionService Community-edition hard caps (prompt 05-07): 5 workflows, 500 runs/month, 1 user.
/// </summary>
public class EditionServiceTests
{
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IOrganisationRepository> _orgs = new();
    private readonly Mock<IPlanRepository> _plans = new();
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowMetricRepository> _metrics = new();
    private readonly Mock<IWorkspaceMemberRepository> _members = new();

    private readonly Guid _ws = Guid.NewGuid();

    private EditionService Community() => Build("community");

    private EditionService Build(string edition)
    {
        var platform = Options.Create(new PlatformOptions { Edition = edition });
        var community = Options.Create(new CommunityOptions { MaxWorkflows = 5, MaxRunsPerMonth = 500, MaxUsers = 1 });
        return new EditionService(platform, community, _workspaces.Object, _orgs.Object, _plans.Object,
            _definitions.Object, _metrics.Object, _members.Object, NullLogger<EditionService>.Instance);
    }

    private void SetWorkflowCount(int count)
    {
        var list = Enumerable.Range(0, count).Select(_ => new WorkflowDefinition { WorkspaceId = _ws }).ToList();
        _definitions.Setup(d => d.GetForWorkspaceAsync(_ws, It.IsAny<CancellationToken>())).ReturnsAsync(list);
    }

    [Fact]
    public async Task Community_at_five_workflows_blocks_the_sixth()
    {
        SetWorkflowCount(5);
        var svc = Community();

        var check = await svc.CheckLimitAsync(_ws, LimitType.WorkflowCount);
        check.LimitReached.Should().BeTrue();
        check.CurrentValue.Should().Be(5);
        check.LimitValue.Should().Be(5);

        var act = async () => await svc.EnsureWithinLimitAsync(_ws, LimitType.WorkflowCount);
        await act.Should().ThrowAsync<EditionLimitException>();
    }

    [Fact]
    public async Task Community_under_five_workflows_allows_creation()
    {
        SetWorkflowCount(4);
        var svc = Community();

        (await svc.CheckLimitAsync(_ws, LimitType.WorkflowCount)).LimitReached.Should().BeFalse();
        await svc.Invoking(s => s.EnsureWithinLimitAsync(_ws, LimitType.WorkflowCount)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Community_499_runs_allowed_but_501_blocked()
    {
        var svc = Community();

        _metrics.Setup(m => m.GetRunCountSinceAsync(_ws, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(499);
        (await svc.CheckLimitAsync(_ws, LimitType.RunsThisMonth)).LimitReached.Should().BeFalse();

        _metrics.Setup(m => m.GetRunCountSinceAsync(_ws, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(501);
        (await svc.CheckLimitAsync(_ws, LimitType.RunsThisMonth)).LimitReached.Should().BeTrue();
    }

    [Fact]
    public async Task Community_member_limit_is_one()
    {
        _members.Setup(m => m.GetForWorkspaceAsync(_ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkspaceMember { WorkspaceId = _ws, IsActive = true }]);
        var svc = Community();

        (await svc.CheckLimitAsync(_ws, LimitType.MemberCount)).LimitReached.Should().BeTrue();
    }

    [Fact]
    public async Task PlanLimitException_body_carries_upgrade_url_and_limit_type()
    {
        SetWorkflowCount(5);
        var svc = Community();

        var ex = await Assert.ThrowsAsync<EditionLimitException>(
            () => svc.EnsureWithinLimitAsync(_ws, LimitType.WorkflowCount));

        ex.LimitTypeKey.Should().Be("workflow_count");
        ex.LimitValue.Should().Be(5);
        ex.HttpStatusCode.Should().Be(429);
        EditionLimitException.UpgradeUrl.Should().Be("https://flowamaz.com/pricing");
    }

    [Fact]
    public async Task Cloud_edition_does_not_hard_block()
    {
        SetWorkflowCount(50);
        var svc = Build("enterprise");

        // Cloud editions surface plan limits via usage/UI but never hard-throw at creation.
        await svc.Invoking(s => s.EnsureWithinLimitAsync(_ws, LimitType.WorkflowCount)).Should().NotThrowAsync();
    }
}
