using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Flowamaz.Infrastructure.Jobs;

/// <summary>
/// Quartz job (daily) that deletes audit events older than the platform's retention period. This is the ONLY place
/// an audit event is ever deleted, and it is server-side, not user-configurable.
/// <para>
/// Retention is the same on every plan: <c>Audit:RetentionDays</c>, default 2,555 days (7 years), never less than
/// 365 (FUNCTIONAL.md audit log: "1 year minimum, configurable up to 7 years"); 0 keeps events forever. Plans only decide how far back the audit log <em>shows</em> (<c>PlanLimits.AuditHistoryDays</c>, enforced
/// by AuditController), so events past a plan's history are still there after an upgrade (FUNCTIONAL.md §13.2).
/// Retention used to follow the plan (30/90/365 days), which deleted exactly the history an upgrade promises.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class AuditRetentionJob : IJob
{
    public const string RetentionDaysKey = "Audit:RetentionDays";
    public const int DefaultRetentionDays = 2555;
    public const int MinimumRetentionDays = 365;

    private readonly FlowAmazDbContext _db;
    private readonly IAuditEventRepository _audit;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuditRetentionJob> _logger;

    public AuditRetentionJob(
        FlowAmazDbContext db,
        IAuditEventRepository audit,
        IConfiguration configuration,
        ILogger<AuditRetentionJob> logger)
    {
        _db = db;
        _audit = audit;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Days audit events are kept; 0 = forever. Missing or negative: the default; below a year: a year.</summary>
    public static int RetentionDays(IConfiguration configuration)
    {
        var days = configuration.GetValue<int?>(RetentionDaysKey);
        if (days is null or < 0) return DefaultRetentionDays;
        return days == 0 ? 0 : Math.Max(MinimumRetentionDays, days.Value);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var retentionDays = RetentionDays(_configuration);
        _logger.LogInformation("AuditRetentionJob enter retentionDays={RetentionDays}", retentionDays);
        if (retentionDays == 0)
        {
            _logger.LogInformation("AuditRetentionJob exit — retention is unlimited, nothing deleted");
            return;
        }

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            // Only orgs that have events past the cutoff need processing.
            var orgIds = await _db.AuditEvents.AsNoTracking()
                .Where(e => e.CreatedAt < cutoff)
                .Select(e => e.OrgId)
                .Distinct()
                .ToListAsync(ct);

            var totalDeleted = 0;
            foreach (var orgId in orgIds)
            {
                var deleted = await _audit.DeleteOlderThanAsync(orgId, cutoff, ct);
                totalDeleted += deleted;
                if (deleted > 0)
                    _logger.LogInformation("AuditRetentionJob org={OrgId} deleted={Deleted}", orgId, deleted);
            }

            _logger.LogInformation("AuditRetentionJob exit totalDeleted={TotalDeleted} orgs={OrgCount}", totalDeleted, orgIds.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AuditRetentionJob error while enforcing audit retention");
        }
    }
}
