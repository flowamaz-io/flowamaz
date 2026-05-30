using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for org billing subscriptions (prompt 07-01). Reads are tracked so webhook handlers
/// can mutate then commit via <see cref="Core.Interfaces.Persistence.IUnitOfWork"/>.
/// </summary>
public sealed class SubscriptionRepository(FlowAmazDbContext db) : ISubscriptionRepository
{
    public Task<Subscription?> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.OrgId == orgId, cancellationToken);

    public Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId, CancellationToken cancellationToken = default) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, cancellationToken);

    public async Task AddAsync(Subscription subscription, CancellationToken cancellationToken = default) =>
        await db.Subscriptions.AddAsync(subscription, cancellationToken);
}
