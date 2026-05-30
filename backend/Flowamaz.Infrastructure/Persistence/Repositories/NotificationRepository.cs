using Flowamaz.Core.Entities.Notifications;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for per-user notifications. Mark-all-read stages tracked updates; the caller commits.</summary>
public sealed class NotificationRepository(FlowAmazDbContext db) : INotificationRepository
{
    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default) =>
        await db.Notifications.AddAsync(notification, cancellationToken);

    public Task<List<Notification>> GetForUserAsync(Guid userId, bool unreadOnly, int limit, CancellationToken cancellationToken = default) =>
        db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && (!unreadOnly || !n.IsRead))
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

    public Task<Notification?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var unread = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(cancellationToken);
        foreach (var notification in unread)
            notification.IsRead = true;
        return unread.Count;
    }
}
