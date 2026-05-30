using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Repositories;

public interface IOrganisationRepository
{
    Task<Organisation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Organisation?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>The org tracked for update (billing webhooks mutate PlanId/Status/StripeCustomerId).</summary>
    Task<Organisation?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Find a tracked org by its Stripe customer id (cus_...). Null if unknown.</summary>
    Task<Organisation?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Stage a new organisation for insertion (persisted on SaveChanges).</summary>
    Task AddAsync(Organisation organisation, CancellationToken cancellationToken = default);

    /// <summary>Stage a new subscription for insertion (persisted on SaveChanges).</summary>
    Task AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default);
}
