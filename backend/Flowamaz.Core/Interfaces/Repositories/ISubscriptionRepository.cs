using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for org billing subscriptions (prompt 07-01). One row per org. Reads are tracked
/// so webhook handlers can mutate-then-save via <see cref="Persistence.IUnitOfWork"/>.
/// </summary>
public interface ISubscriptionRepository
{
    /// <summary>The org's subscription, tracked for update. Null if the org has never had one.</summary>
    Task<Subscription?> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>Find by Stripe subscription id (sub_...), tracked. Null if unknown.</summary>
    Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Stage a new subscription for insertion (persisted on SaveChanges).</summary>
    Task AddAsync(Subscription subscription, CancellationToken cancellationToken = default);
}
