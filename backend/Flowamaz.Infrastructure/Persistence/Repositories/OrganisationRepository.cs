using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for organisations and their subscriptions. Add* only stage entities;
/// the caller commits via <see cref="Core.Interfaces.Persistence.IUnitOfWork"/>.
/// </summary>
public sealed class OrganisationRepository(FlowAmazDbContext db) : IOrganisationRepository
{
    public Task<Organisation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Organisation?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Slug == slug, cancellationToken);

    public Task<Organisation?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Organisations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Organisation?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken cancellationToken = default) =>
        db.Organisations.FirstOrDefaultAsync(o => o.StripeCustomerId == stripeCustomerId, cancellationToken);

    public Task<List<Organisation>> SearchAsync(string? search, int take, CancellationToken cancellationToken = default)
    {
        var query = db.Organisations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(o => EF.Functions.ILike(o.Name, pattern, "\\") || EF.Functions.ILike(o.Slug, pattern, "\\"));
        }
        return query.OrderBy(o => o.Name).Take(take).ToListAsync(cancellationToken);
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        db.Organisations.AnyAsync(o => o.Slug == slug, cancellationToken);

    public async Task AddAsync(Organisation organisation, CancellationToken cancellationToken = default) =>
        await db.Organisations.AddAsync(organisation, cancellationToken);

    public async Task AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default) =>
        await db.Subscriptions.AddAsync(subscription, cancellationToken);
}
