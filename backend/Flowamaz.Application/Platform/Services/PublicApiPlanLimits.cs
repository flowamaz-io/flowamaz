using System.Collections.Concurrent;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Platform.Services;

/// <summary>
/// Resolves workspace → organisation → plan to the per-minute public API limit (FUNCTIONAL.md §13.2:
/// Community 10, Starter 60, Pro 300, Enterprise custom). An organisation's own
/// <c>ApiRateLimitPerMinute</c> overrides its plan. Results are cached for a minute (see
/// <see cref="PublicApiPlanLimitCache"/>) so the public API doesn't read three tables per call; a plan change
/// takes effect within that minute.
/// </summary>
public sealed class PublicApiPlanLimits : IPublicApiPlanLimits
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IOrganisationRepository _organisations;
    private readonly IPlanRepository _plans;
    private readonly PublicApiPlanLimitCache _cache;
    private readonly ILogger<PublicApiPlanLimits> _logger;

    public PublicApiPlanLimits(
        IWorkspaceRepository workspaces, IOrganisationRepository organisations, IPlanRepository plans,
        PublicApiPlanLimitCache cache, ILogger<PublicApiPlanLimits> logger)
    {
        _workspaces = workspaces;
        _organisations = organisations;
        _plans = plans;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PlanApiLimit?> ResolveAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGet(workspaceId, out var cached)) return cached;
        _logger.LogDebug("PublicApiPlanLimits.ResolveAsync enter workspace={WorkspaceId}", workspaceId);
        try
        {
            var workspace = await _workspaces.GetByIdAsync(workspaceId, cancellationToken);
            if (workspace is null) return null;
            var org = await _organisations.GetByIdAsync(workspace.OrgId, cancellationToken);
            if (org is null) return null;
            var plan = await _plans.GetByIdAsync(org.PlanId, cancellationToken);

            var perMinute = org.ApiRateLimitPerMinute ?? plan?.Limits.ApiRateLimitPerMinute ?? 0;
            var limit = new PlanApiLimit(org.Id, plan?.Name ?? "current", Math.Max(0, perMinute),
                IsCustom: org.ApiRateLimitPerMinute is not null);
            _cache.Set(workspaceId, limit);
            _logger.LogDebug("PublicApiPlanLimits.ResolveAsync exit workspace={WorkspaceId} plan={Plan} perMinute={PerMinute}",
                workspaceId, limit.PlanName, limit.PerMinute);
            return limit;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PublicApiPlanLimits.ResolveAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }
}

/// <summary>Process-wide one-minute cache of resolved plan limits, keyed by workspace.</summary>
public sealed class PublicApiPlanLimitCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<Guid, (PlanApiLimit Limit, DateTime Expires)> _entries = new();
    private readonly TimeProvider _clock;

    public PublicApiPlanLimitCache(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public bool TryGet(Guid workspaceId, out PlanApiLimit? limit)
    {
        if (_entries.TryGetValue(workspaceId, out var e) && e.Expires > _clock.GetUtcNow().UtcDateTime)
        {
            limit = e.Limit;
            return true;
        }
        limit = null;
        return false;
    }

    public void Set(Guid workspaceId, PlanApiLimit limit) =>
        _entries[workspaceId] = (limit, _clock.GetUtcNow().UtcDateTime.Add(Ttl));
}
