using Flowamaz.Core.Entities.Notifications;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for per-user key/value preferences.</summary>
public sealed class UserPreferenceRepository(FlowAmazDbContext db) : IUserPreferenceRepository
{
    public Task<UserPreference?> GetAsync(Guid userId, string key, CancellationToken cancellationToken = default) =>
        db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key, cancellationToken);

    public async Task AddAsync(UserPreference preference, CancellationToken cancellationToken = default) =>
        await db.UserPreferences.AddAsync(preference, cancellationToken);

    public void Update(UserPreference preference) => db.UserPreferences.Update(preference);
}
