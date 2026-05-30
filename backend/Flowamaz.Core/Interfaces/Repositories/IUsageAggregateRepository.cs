using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Read access to the per-org monthly usage rollup (prompt 07-01 billing usage).</summary>
public interface IUsageAggregateRepository
{
    /// <summary>The org's usage row for the given period ("yyyy-MM"), or null if none yet.</summary>
    Task<UsageAggregate?> GetForPeriodAsync(Guid orgId, string periodMonth, CancellationToken cancellationToken = default);
}
