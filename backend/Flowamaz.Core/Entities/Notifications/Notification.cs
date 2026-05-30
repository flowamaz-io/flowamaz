using Flowamaz.Core.Entities;

namespace Flowamaz.Core.Entities.Notifications;

/// <summary>
/// An in-app notification for a single user. Created by platform events (instance terminal state,
/// gate pending/decided, member invited, trial expiring) and read via the notification bell.
/// </summary>
public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid OrgId { get; set; }
    public Guid? WorkspaceId { get; set; }

    /// <summary>instance_complete | gate_pending | gate_decided | member_invited | trial_expiring.</summary>
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
}
