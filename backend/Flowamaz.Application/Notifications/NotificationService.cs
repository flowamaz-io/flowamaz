using Flowamaz.Core.Entities.Notifications;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Notifications;

/// <summary>
/// In-app notification centre. Notify* helpers compose a typed notification and persist it; they
/// never throw to the caller (firing is best-effort so a notification failure can't break the
/// workflow/gate/member operation that triggered it).
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(INotificationRepository repo, IUnitOfWork unitOfWork, ILogger<NotificationService> logger)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NotificationDto>> GetForUserAsync(Guid userId, bool unreadOnly, int limit, CancellationToken ct = default)
    {
        var items = await _repo.GetForUserAsync(userId, unreadOnly, Math.Clamp(limit, 1, 100), ct);
        return items.Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.ActionUrl, n.IsRead, n.CreatedAt)).ToList();
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default) => _repo.CountUnreadAsync(userId, ct);

    public async Task MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var notification = await _repo.GetByIdForUserAsync(id, userId, ct);
        if (notification is null || notification.IsRead) return;
        notification.IsRead = true;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var count = await _repo.MarkAllReadAsync(userId, ct);
        if (count > 0) await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogDebug("NotificationService.MarkAllReadAsync user={UserId} marked={Count}", userId, count);
    }

    public async Task CreateAsync(Notification notification, CancellationToken ct = default)
    {
        try
        {
            await _repo.AddAsync(notification, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Best-effort: never propagate a notification failure to the originating operation.
            _logger.LogError(ex, "NotificationService.CreateAsync failed user={UserId} type={Type}", notification.UserId, notification.Type);
        }
    }

    public Task NotifyInstanceCompleteAsync(
        Guid userId, Guid orgId, Guid workspaceId, Guid instanceId, InstanceStatus status, CancellationToken ct = default)
    {
        var ok = status == InstanceStatus.Completed;
        return CreateAsync(new Notification
        {
            UserId = userId, OrgId = orgId, WorkspaceId = workspaceId, Type = "instance_complete",
            Title = ok ? "Workflow completed" : "Workflow failed",
            Message = ok
                ? "A workflow run you started finished successfully."
                : $"A workflow run you started ended with status {status}.",
            ActionUrl = $"/instances/{instanceId}",
        }, ct);
    }

    public Task NotifyGatePendingAsync(
        Guid assigneeUserId, Guid orgId, Guid workspaceId, Guid instanceId, string nodeId, CancellationToken ct = default) =>
        CreateAsync(new Notification
        {
            UserId = assigneeUserId, OrgId = orgId, WorkspaceId = workspaceId, Type = "gate_pending",
            Title = "Approval needed",
            Message = "A workflow is waiting for your approval.",
            ActionUrl = $"/instances/{instanceId}?gate={nodeId}",
        }, ct);

    public Task NotifyGateDecidedAsync(
        Guid requestorUserId, Guid orgId, Guid workspaceId, Guid instanceId, string decision, CancellationToken ct = default) =>
        CreateAsync(new Notification
        {
            UserId = requestorUserId, OrgId = orgId, WorkspaceId = workspaceId, Type = "gate_decided",
            Title = $"Approval {decision}",
            Message = $"Your approval request was {decision}.",
            ActionUrl = $"/instances/{instanceId}",
        }, ct);

    public Task NotifyMemberInvitedAsync(
        Guid adminUserId, Guid orgId, Guid workspaceId, string invitedEmail, CancellationToken ct = default) =>
        CreateAsync(new Notification
        {
            UserId = adminUserId, OrgId = orgId, WorkspaceId = workspaceId, Type = "member_invited",
            Title = "New team member",
            Message = $"{invitedEmail} was added to a workspace.",
            ActionUrl = "/settings/members",
        }, ct);
}
