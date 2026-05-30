using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (daily) that enforces audit-log retention per plan. This is the ONLY place an audit
/// event is ever deleted — retention is server-side and not user-configurable.
/// Community/Starter: 30 days · Pro: 90 days · Enterprise: 365 days.
/// </summary>
[DisallowConcurrentExecution]
public sealed class AuditRetentionJob : IJob
{
    private readonly FlowAmazDbContext _db;
    private readonly IAuditEventRepository _audit;
    private readonly IOrganisationRepository _organisations;
    private readonly IPlanRepository _plans;
    private readonly ILogger<AuditRetentionJob> _logger;

    public AuditRetentionJob(
        FlowAmazDbContext db,
        IAuditEventRepository audit,
        IOrganisationRepository organisations,
        IPlanRepository plans,
        ILogger<AuditRetentionJob> logger)
    {
        _db = db;
        _audit = audit;
        _organisations = organisations;
        _plans = plans;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        _logger.LogInformation("AuditRetentionJob enter");
        try
        {
            // Only orgs that actually have audit events need processing.
            var orgIds = await _db.AuditEvents.AsNoTracking()
                .Select(e => e.OrgId)
                .Distinct()
                .ToListAsync(ct);

            var totalDeleted = 0;
            foreach (var orgId in orgIds)
            {
                var retentionDays = await ResolveRetentionDaysAsync(orgId, ct);
                var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
                var deleted = await _audit.DeleteOlderThanAsync(orgId, cutoff, ct);
                if (deleted > 0)
                {
                    totalDeleted += deleted;
                    _logger.LogInformation(
                        "AuditRetentionJob org={OrgId} retentionDays={RetentionDays} deleted={Deleted}",
                        orgId, retentionDays, deleted);
                }
            }

            _logger.LogInformation("AuditRetentionJob exit totalDeleted={TotalDeleted} orgs={OrgCount}", totalDeleted, orgIds.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AuditRetentionJob error while enforcing audit retention");
        }
    }

    private async Task<int> ResolveRetentionDaysAsync(Guid orgId, CancellationToken ct)
    {
        var org = await _organisations.GetByIdAsync(orgId, ct);
        if (org is null) return 30;
        var plan = await _plans.GetByIdAsync(org.PlanId, ct);
        return RetentionDaysForPlan(plan?.Slug);
    }

    /// <summary>Retention window in days for a plan slug. Defaults to the most conservative (30d).</summary>
    public static int RetentionDaysForPlan(string? planSlug) => planSlug?.ToLowerInvariant() switch
    {
        "enterprise" => 365,
        "pro" => 90,
        _ => 30, // community + starter (and anything unknown)
    };
}
