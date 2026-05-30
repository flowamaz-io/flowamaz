using Flowamaz.Core.Entities.Notifications;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Persistence for per-user key/value preferences.</summary>
public interface IUserPreferenceRepository
{
    Task<UserPreference?> GetAsync(Guid userId, string key, CancellationToken cancellationToken = default);
    Task AddAsync(UserPreference preference, CancellationToken cancellationToken = default);
    void Update(UserPreference preference);
}
