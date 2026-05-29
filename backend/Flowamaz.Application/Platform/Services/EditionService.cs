using Flowamaz.Core.Configuration;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowamaz.Application.Platform.Services;

/// <summary>
/// Enforces edition limits (prompt 05-07). Community edition uses the hard caps from the Community
/// config section; cloud editions resolve the org's Plan limits (workspace → org → plan). Limit
/// breaches throw <see cref="EditionLimitException"/> → HTTP 429.
/// </summary>
public sealed class EditionService : IEditionService
{
    private readonly CommunityOptions _community;
    private readonly IWorkspaceRepository _workspaces;
    private readonly IOrganisationRepository _organisations;
    private readonly IPlanRepository _plans;
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowMetricRepository _metrics;
    private readonly IWorkspaceMemberRepository _members;
    private readonly ILogger<EditionService> _logger;

    public EditionService(
        IOptions<PlatformOptions> platform,
        IOptions<CommunityOptions> community,
        IWorkspaceRepository workspaces,
        IOrganisationRepository organisations,
        IPlanRepository plans,
        IWorkflowDefinitionRepository definitions,
        IWorkflowMetricRepository metrics,
        IWorkspaceMemberRepository members,
        ILogger<EditionService> logger)
    {
        Edition = string.IsNullOrWhiteSpace(platform.Value.Edition) ? "community" : platform.Value.Edition.ToLowerInvariant();
        _community = community.Value;
        _workspaces = workspaces;
        _organisations = organisations;
        _plans = plans;
        _definitions = definitions;
        _metrics = metrics;
        _members = members;
        _logger = logger;
    }

    public string Edition { get; }

    public bool IsCommunity => Edition == "community";

    public Task<EditionLimits> GetEditionLimitsAsync(CancellationToken ct = default) =>
        Task.FromResult(IsCommunity
            ? new EditionLimits(Edition, _community.MaxWorkflows, _community.MaxRunsPerMonth, _community.MaxUsers)
            // Cloud limits are per-plan and resolved per-workspace in CheckLimitAsync; report unlimited here.
            : new EditionLimits(Edition, int.MaxValue, long.MaxValue, int.MaxValue));

    public async Task<LimitCheckResult> CheckLimitAsync(Guid workspaceId, LimitType limitType, CancellationToken ct = default)
    {
        var limit = await ResolveLimitAsync(workspaceId, limitType, ct);
        var current = await CurrentValueAsync(workspaceId, limitType, ct);
        // "At limit" means creating one more would exceed: current >= limit. Unlimited (<0 or MaxValue) never blocks.
        var reached = limit is > 0 and < long.MaxValue && current >= limit;
        return new LimitCheckResult(limitType, reached, current, limit, Edition);
    }

    public async Task EnsureWithinLimitAsync(Guid workspaceId, LimitType limitType, CancellationToken ct = default)
    {
        // Hard caps are a Community-edition feature. Cloud editions surface plan limits via the usage
        // endpoint/UI and meter overage through billing — they do not hard-block resource creation.
        if (!IsCommunity) return;

        var check = await CheckLimitAsync(workspaceId, limitType, ct);
        if (!check.LimitReached) return;

        _logger.LogInformation(
            "EditionService.EnsureWithinLimitAsync blocked workspace={WorkspaceId} limit={LimitType} current={Current} max={Max} edition={Edition}",
            workspaceId, limitType, check.CurrentValue, check.LimitValue, Edition);

        var message = limitType switch
        {
            LimitType.WorkflowCount => $"Community edition limit: {check.LimitValue} workflows. Upgrade to Starter for unlimited workflows.",
            LimitType.RunsThisMonth => $"Community edition limit: {check.LimitValue} runs per month. Upgrade to Starter for more runs.",
            LimitType.MemberCount => $"Community edition limit: {check.LimitValue} user. Upgrade to Starter to invite your team.",
            _ => $"Community edition limit reached ({check.LimitValue}). Upgrade for more capacity.",
        };
        throw new EditionLimitException(limitType, check.CurrentValue, check.LimitValue, message);
    }

    private async Task<long> ResolveLimitAsync(Guid workspaceId, LimitType limitType, CancellationToken ct)
    {
        if (IsCommunity)
        {
            return limitType switch
            {
                LimitType.WorkflowCount => _community.MaxWorkflows,
                LimitType.RunsThisMonth => _community.MaxRunsPerMonth,
                LimitType.MemberCount => _community.MaxUsers,
                _ => long.MaxValue,
            };
        }

        // Cloud: workspace → org → plan limits.
        var plan = await ResolvePlanAsync(workspaceId, ct);
        if (plan is null) return long.MaxValue;
        return limitType switch
        {
            LimitType.WorkflowCount => plan.Limits.MaxWorkflowDefinitions,
            LimitType.RunsThisMonth => plan.Limits.MaxRunsPerMonth,
            LimitType.MemberCount => plan.Limits.MaxMembersPerWorkspace,
            _ => long.MaxValue,
        };
    }

    private async Task<Core.Entities.Platform.Plan?> ResolvePlanAsync(Guid workspaceId, CancellationToken ct)
    {
        var workspace = await _workspaces.GetByIdAsync(workspaceId, ct);
        if (workspace is null) return null;
        var org = await _organisations.GetByIdAsync(workspace.OrgId, ct);
        if (org is null) return null;
        return await _plans.GetByIdAsync(org.PlanId, ct);
    }

    private async Task<long> CurrentValueAsync(Guid workspaceId, LimitType limitType, CancellationToken ct)
    {
        switch (limitType)
        {
            case LimitType.WorkflowCount:
                return (await _definitions.GetForWorkspaceAsync(workspaceId, ct)).Count;
            case LimitType.RunsThisMonth:
                var now = DateTime.UtcNow;
                var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                return await _metrics.GetRunCountSinceAsync(workspaceId, monthStart, ct);
            case LimitType.MemberCount:
                return (await _members.GetForWorkspaceAsync(workspaceId, ct)).Count(m => m.IsActive);
            default:
                return 0;
        }
    }
}
