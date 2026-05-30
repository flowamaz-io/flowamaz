using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workspace.Services;

/// <summary>
/// Workspace membership management. Three invariants are enforced here (service layer), so they
/// hold no matter which caller invokes them: no self-role-change, no self-removal, and the
/// workspace must always retain at least one active Admin.
/// </summary>
public sealed class WorkspaceMemberService : IWorkspaceMemberService
{
    private readonly IWorkspaceMemberRepository _memberRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEditionService _edition;
    private readonly ILogger<WorkspaceMemberService> _logger;
    private readonly IAuditService? _audit;

    public WorkspaceMemberService(
        IWorkspaceMemberRepository memberRepository,
        IUnitOfWork unitOfWork,
        IEditionService edition,
        ILogger<WorkspaceMemberService> logger,
        IAuditService? audit = null)
    {
        _memberRepository = memberRepository;
        _unitOfWork = unitOfWork;
        _edition = edition;
        _logger = logger;
        _audit = audit;
    }

    public async Task<WorkspaceMember> AddMemberAsync(
        Guid workspaceId, Guid orgUserId, WorkspaceRole role, Guid addedByUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkspaceMemberService.AddMemberAsync enter workspaceId={WorkspaceId} orgUserId={OrgUserId} role={Role} addedBy={AddedBy}",
            workspaceId, orgUserId, role, addedByUserId);
        try
        {
            var existing = await _memberRepository.GetAsync(workspaceId, orgUserId, cancellationToken);
            if (existing is not null)
            {
                throw new InvalidOperationException(
                    $"User '{orgUserId}' is already a member of workspace '{workspaceId}'. " +
                    "Update their role instead of adding them again.");
            }

            // Edition gate (prompt 05-07): Community edition caps the active member count.
            await _edition.EnsureWithinLimitAsync(workspaceId, LimitType.MemberCount, cancellationToken);

            var member = new WorkspaceMember
            {
                WorkspaceId = workspaceId,
                OrgUserId = orgUserId,
                Role = role,
                IsActive = true,
            };

            await _memberRepository.AddAsync(member, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _audit?.RecordAsync(new AuditEventRequest
            {
                WorkspaceId = workspaceId,
                ActorUserId = addedByUserId,
                ActorType = "user",
                EventType = "member.invited",
                ResourceType = "member",
                ResourceId = orgUserId,
                Action = "invited",
                Metadata = new { role = role.ToString() },
            }, cancellationToken);

            _logger.LogInformation(
                "WorkspaceMemberService.AddMemberAsync exit workspaceId={WorkspaceId} orgUserId={OrgUserId} role={Role}",
                workspaceId, orgUserId, role);
            return member;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WorkspaceMemberService.AddMemberAsync error workspaceId={WorkspaceId} orgUserId={OrgUserId}", workspaceId, orgUserId);
            throw;
        }
    }

    public async Task UpdateRoleAsync(
        Guid workspaceId, Guid targetUserId, WorkspaceRole newRole, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkspaceMemberService.UpdateRoleAsync enter workspaceId={WorkspaceId} target={Target} newRole={NewRole} requestedBy={Requester}",
            workspaceId, targetUserId, newRole, requestingUserId);
        try
        {
            if (requestingUserId == targetUserId)
            {
                throw new SelfModificationException("change the role of");
            }

            var member = await _memberRepository.GetAsync(workspaceId, targetUserId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"User '{targetUserId}' is not a member of workspace '{workspaceId}'.");

            var isDemotingLastAdmin = member.Role == WorkspaceRole.Admin
                && member.IsActive
                && newRole != WorkspaceRole.Admin;
            if (isDemotingLastAdmin && await _memberRepository.CountActiveAdminsAsync(workspaceId, cancellationToken) <= 1)
            {
                throw new LastAdminException();
            }

            member.Role = newRole;
            _memberRepository.Update(member);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "WorkspaceMemberService.UpdateRoleAsync exit workspaceId={WorkspaceId} target={Target} newRole={NewRole}",
                workspaceId, targetUserId, newRole);
        }
        catch (Exception ex) when (ex is not SelfModificationException and not LastAdminException)
        {
            _logger.LogError(ex,
                "WorkspaceMemberService.UpdateRoleAsync error workspaceId={WorkspaceId} target={Target}", workspaceId, targetUserId);
            throw;
        }
    }

    public async Task RemoveMemberAsync(
        Guid workspaceId, Guid targetUserId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkspaceMemberService.RemoveMemberAsync enter workspaceId={WorkspaceId} target={Target} requestedBy={Requester}",
            workspaceId, targetUserId, requestingUserId);
        try
        {
            if (requestingUserId == targetUserId)
            {
                throw new SelfModificationException("remove");
            }

            var member = await _memberRepository.GetAsync(workspaceId, targetUserId, cancellationToken);
            if (member is null)
            {
                _logger.LogWarning(
                    "WorkspaceMemberService.RemoveMemberAsync no-op workspaceId={WorkspaceId} target={Target} — not a member",
                    workspaceId, targetUserId);
                return;
            }

            var isRemovingLastAdmin = member.Role == WorkspaceRole.Admin && member.IsActive;
            if (isRemovingLastAdmin && await _memberRepository.CountActiveAdminsAsync(workspaceId, cancellationToken) <= 1)
            {
                throw new LastAdminException();
            }

            _memberRepository.Remove(member);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "WorkspaceMemberService.RemoveMemberAsync exit workspaceId={WorkspaceId} target={Target}", workspaceId, targetUserId);
        }
        catch (Exception ex) when (ex is not SelfModificationException and not LastAdminException)
        {
            _logger.LogError(ex,
                "WorkspaceMemberService.RemoveMemberAsync error workspaceId={WorkspaceId} target={Target}", workspaceId, targetUserId);
            throw;
        }
    }

    public async Task LeaveWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkspaceMemberService.LeaveWorkspaceAsync enter workspaceId={WorkspaceId} user={UserId}",
            workspaceId, userId);
        try
        {
            var member = await _memberRepository.GetAsync(workspaceId, userId, cancellationToken);
            if (member is null)
            {
                _logger.LogWarning(
                    "WorkspaceMemberService.LeaveWorkspaceAsync no-op workspaceId={WorkspaceId} user={UserId} — not a member",
                    workspaceId, userId);
                return;
            }

            var isLastAdmin = member.Role == WorkspaceRole.Admin && member.IsActive;
            if (isLastAdmin && await _memberRepository.CountActiveAdminsAsync(workspaceId, cancellationToken) <= 1)
            {
                throw new LastAdminException();
            }

            _memberRepository.Remove(member);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "WorkspaceMemberService.LeaveWorkspaceAsync exit workspaceId={WorkspaceId} user={UserId}", workspaceId, userId);
        }
        catch (Exception ex) when (ex is not LastAdminException)
        {
            _logger.LogError(ex,
                "WorkspaceMemberService.LeaveWorkspaceAsync error workspaceId={WorkspaceId} user={UserId}", workspaceId, userId);
            throw;
        }
    }

    public async Task<List<WorkspaceMemberDto>> GetMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceMemberService.GetMembersAsync enter workspaceId={WorkspaceId}", workspaceId);
        try
        {
            var members = await _memberRepository.GetForWorkspaceAsync(workspaceId, cancellationToken);
            var dtos = members
                .Select(m => new WorkspaceMemberDto(m.WorkspaceId, m.OrgUserId, m.Role, m.JoinedAt, m.IsActive))
                .ToList();
            _logger.LogDebug("WorkspaceMemberService.GetMembersAsync exit workspaceId={WorkspaceId} count={Count}", workspaceId, dtos.Count);
            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceMemberService.GetMembersAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task<List<WorkspaceMemberDetailDto>> GetDetailedMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceMemberService.GetDetailedMembersAsync enter workspaceId={WorkspaceId}", workspaceId);
        try
        {
            var members = await _memberRepository.GetDetailedMembersAsync(workspaceId, cancellationToken);
            _logger.LogDebug("WorkspaceMemberService.GetDetailedMembersAsync exit workspaceId={WorkspaceId} count={Count}", workspaceId, members.Count);
            return members;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceMemberService.GetDetailedMembersAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task<WorkspaceRole?> GetMemberRoleAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceMemberService.GetMemberRoleAsync enter workspaceId={WorkspaceId} orgUserId={OrgUserId}", workspaceId, orgUserId);
        try
        {
            var member = await _memberRepository.GetAsync(workspaceId, orgUserId, cancellationToken);
            var role = member is { IsActive: true } ? member.Role : (WorkspaceRole?)null;
            _logger.LogDebug(
                "WorkspaceMemberService.GetMemberRoleAsync exit workspaceId={WorkspaceId} orgUserId={OrgUserId} role={Role}",
                workspaceId, orgUserId, role);
            return role;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceMemberService.GetMemberRoleAsync error workspaceId={WorkspaceId} orgUserId={OrgUserId}", workspaceId, orgUserId);
            throw;
        }
    }

    public async Task<bool> IsMemberAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceMemberService.IsMemberAsync enter workspaceId={WorkspaceId} orgUserId={OrgUserId}", workspaceId, orgUserId);
        try
        {
            var member = await _memberRepository.GetAsync(workspaceId, orgUserId, cancellationToken);
            var isMember = member is { IsActive: true };
            _logger.LogDebug(
                "WorkspaceMemberService.IsMemberAsync exit workspaceId={WorkspaceId} orgUserId={OrgUserId} isMember={IsMember}",
                workspaceId, orgUserId, isMember);
            return isMember;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceMemberService.IsMemberAsync error workspaceId={WorkspaceId} orgUserId={OrgUserId}", workspaceId, orgUserId);
            throw;
        }
    }
}
