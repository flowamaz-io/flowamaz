using Flowamaz.Core.Entities.Notifications;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Persistence for per-user in-app notifications.</summary>
public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);
    Task<List<Notification>> GetForUserAsync(Guid userId, bool unreadOnly, int limit, CancellationToken cancellationToken = default);
    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Notification?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Marks every unread notification for the user as read. Returns the number updated.</summary>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
