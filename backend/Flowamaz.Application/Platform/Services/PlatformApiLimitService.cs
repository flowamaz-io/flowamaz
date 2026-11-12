using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Platform.Services;

/// <summary>An organisation with its public API limit, for the platform staff screen.</summary>
public sealed record PlatformOrgApiLimitDto(
    Guid OrgId, string Name, string Slug, string PlanName,
    /// <summary>What the plan allows per minute.</summary>
    int PlanPerMinute,
    /// <summary>The limit agreed for this organisation, or null when the plan's applies. 0 means no per-minute limit.</summary>
    int? CustomPerMinute,
    /// <summary>The limit in force.</summary>
    int EffectivePerMinute);

/// <summary>
/// Flowamaz staff set the public API limit agreed with an organisation (Enterprise "1,200+ … higher on request").
/// Organisation owners see the limit but can't change it. Callers check <see cref="PlatformStaff"/> first.
/// </summary>
public sealed class PlatformApiLimitService
{
    public const int MaxPerMinute = 100_000;
    private const int PageSize = 50;

    private readonly IOrganisationRepository _orgs;
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PublicApiPlanLimitCache _cache;
    private readonly ILogger<PlatformApiLimitService> _logger;
    private readonly IAuditService? _audit;

    public PlatformApiLimitService(
        IOrganisationRepository orgs, IPlanRepository plans, IUnitOfWork unitOfWork, PublicApiPlanLimitCache cache,
        ILogger<PlatformApiLimitService> logger, IAuditService? audit = null)
    {
        _orgs = orgs;
        _plans = plans;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
        _audit = audit;
    }

    public async Task<List<PlatformOrgApiLimitDto>> SearchAsync(string? search, CancellationToken ct = default)
    {
        _logger.LogDebug("PlatformApiLimitService.SearchAsync enter");
        try
        {
            var orgs = await _orgs.SearchAsync(search, PageSize, ct);
            var result = new List<PlatformOrgApiLimitDto>(orgs.Count);
            foreach (var org in orgs) result.Add(await ToDtoAsync(org, ct));
            _logger.LogDebug("PlatformApiLimitService.SearchAsync exit count={Count}", result.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "PlatformApiLimitService.SearchAsync error");
            throw;
        }
    }

    /// <summary>
    /// Sets the agreed limit (<paramref name="perMinute"/>: 0 = no per-minute limit), or with null goes back to the
    /// plan's. Applies on this server at once and on the others within a minute. Null when there is no such organisation.
    /// </summary>
    public async Task<PlatformOrgApiLimitDto?> SetAsync(Guid orgId, int? perMinute, Guid staffUserId, CancellationToken ct = default)
    {
        _logger.LogInformation("PlatformApiLimitService.SetAsync enter org={OrgId} perMinute={PerMinute} staff={StaffId}", orgId, perMinute, staffUserId);
        try
        {
            if (perMinute is < 0 or > MaxPerMinute)
                throw new ApiLimitOutOfRangeException(MaxPerMinute);
            var org = await _orgs.GetForUpdateAsync(orgId, ct);
            if (org is null) return null;
            var before = org.ApiRateLimitPerMinute;
            org.ApiRateLimitPerMinute = perMinute;
            await _unitOfWork.SaveChangesAsync(ct);
            _cache.InvalidateOrg(orgId);

            _audit?.RecordAsync(new Core.Models.AuditEventRequest
            {
                OrgId = orgId,
                ActorType = "system",
                ActorLabel = "Flowamaz support",
                EventType = "org.api_limit_changed",
                ResourceType = "organisation",
                ResourceId = orgId,
                ResourceLabel = org.Name,
                Action = "updated",
                Metadata = new { from = before, to = perMinute, staffUserId },
            }, ct);
            var dto = await ToDtoAsync(org, ct);
            _logger.LogInformation("PlatformApiLimitService.SetAsync exit org={OrgId} effective={Effective}", orgId, dto.EffectivePerMinute);
            return dto;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ApiLimitOutOfRangeException)
        {
            _logger.LogError(ex, "PlatformApiLimitService.SetAsync error org={OrgId}", orgId);
            throw;
        }
    }

    private async Task<PlatformOrgApiLimitDto> ToDtoAsync(Core.Entities.Platform.Organisation org, CancellationToken ct)
    {
        var plan = await _plans.GetByIdAsync(org.PlanId, ct);
        var planLimit = plan?.Limits.ApiRateLimitPerMinute ?? 0;
        return new PlatformOrgApiLimitDto(org.Id, org.Name, org.Slug, plan?.Name ?? "Unknown plan", planLimit,
            org.ApiRateLimitPerMinute, org.ApiRateLimitPerMinute ?? planLimit);
    }
}
