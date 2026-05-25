using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Read access to seeded plan reference data. Reads are no-tracking.</summary>
public sealed class PlanRepository(FlowAmazDbContext db) : IPlanRepository
{
    public Task<Plan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Plan?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken);
}
