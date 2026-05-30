using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Read access to the per-org monthly usage rollup (prompt 07-01). Reads are no-tracking.</summary>
public sealed class UsageAggregateRepository(FlowAmazDbContext db) : IUsageAggregateRepository
{
    public Task<UsageAggregate?> GetForPeriodAsync(Guid orgId, string periodMonth, CancellationToken cancellationToken = default) =>
        db.UsageAggregates.AsNoTracking()
            .FirstOrDefaultAsync(u => u.OrgId == orgId && u.PeriodMonth == periodMonth, cancellationToken);
}
