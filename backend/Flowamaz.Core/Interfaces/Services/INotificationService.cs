using Flowamaz.Core.Entities.Notifications;
using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>A notification as returned to the bell dropdown.</summary>
public sealed record NotificationDto(
    Guid Id, string Type, string Title, string Message, string? ActionUrl, bool IsRead, DateTime CreatedAt);

/// <summary>
/// In-app notification centre. Read paths are per current user; the Notify* methods are called by
/// platform events (workflow terminal state, gate pending/decided, member invited) and are
/// best-effort — a notification failure must never break the originating operation.
/// </summary>
public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetForUserAsync(Guid userId, bool unreadOnly, int limit, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);
    Task MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    Task NotifyInstanceCompleteAsync(Guid userId, Guid orgId, Guid workspaceId, Guid instanceId, InstanceStatus status, CancellationToken ct = default);
    Task NotifyGatePendingAsync(Guid assigneeUserId, Guid orgId, Guid workspaceId, Guid instanceId, string nodeId, CancellationToken ct = default);
    Task NotifyGateDecidedAsync(Guid requestorUserId, Guid orgId, Guid workspaceId, Guid instanceId, string decision, CancellationToken ct = default);
    Task NotifyMemberInvitedAsync(Guid adminUserId, Guid orgId, Guid workspaceId, string invitedEmail, CancellationToken ct = default);

    /// <summary>Persists a notification directly (used by the typed Notify* helpers).</summary>
    Task CreateAsync(Notification notification, CancellationToken ct = default);
}
