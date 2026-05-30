using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>The in-app notification centre for the current user (bell dropdown).</summary>
[ApiController]
[Route("api/v1/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(INotificationService notifications, ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    private Guid UserId => _currentUser.UserId
        ?? throw new UnauthorizedAccessException("Sign in to view your notifications.");

    /// <summary>Lists the current user's notifications, optionally filtered to unread only and capped by a limit.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(
        [FromQuery] bool unread = false, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await _notifications.GetForUserAsync(UserId, unread, limit, ct));

    /// <summary>Returns the count of unread notifications for the current user (used for the bell badge).</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken ct) =>
        Ok(await _notifications.GetUnreadCountAsync(UserId, ct));

    /// <summary>Marks a single notification as read for the current user.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(id, UserId, ct);
        return NoContent();
    }

    /// <summary>Marks all of the current user's notifications as read.</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(UserId, ct);
        return NoContent();
    }
}
