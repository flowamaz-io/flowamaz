using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Repositories;

public interface IOrganisationRepository
{
    Task<Organisation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Organisation?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Stage a new organisation for insertion (persisted on SaveChanges).</summary>
    Task AddAsync(Organisation organisation, CancellationToken cancellationToken = default);

    /// <summary>Stage a new subscription for insertion (persisted on SaveChanges).</summary>
    Task AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default);
}
