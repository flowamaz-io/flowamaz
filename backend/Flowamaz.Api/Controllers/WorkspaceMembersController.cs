using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workspace.DTOs;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workspace membership management — all Admin-gated. Members are existing org users looked up by
/// email (no email invitations in Phase 1). Self-modification and last-admin rules are enforced by
/// the member service.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/members")]
public sealed class WorkspaceMembersController : ControllerBase
{
    private readonly IWorkspaceMemberService _memberService;
    private readonly IOrgUserService _orgUserService;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<AddMemberRequest> _addValidator;

    public WorkspaceMembersController(
        IWorkspaceMemberService memberService,
        IOrgUserService orgUserService,
        ICurrentUserService currentUser,
        IValidator<AddMemberRequest> addValidator)
    {
        _memberService = memberService;
        _orgUserService = orgUserService;
        _currentUser = currentUser;
        _addValidator = addValidator;
    }

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<PagedResult<MemberResponse>>> List(
        Guid workspaceId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var members = await _memberService.GetDetailedMembersAsync(workspaceId, cancellationToken);
        var items = members
            .Select(m => new MemberResponse(m.OrgUserId, m.Email, m.Name, m.Role, m.JoinedAt, m.IsActive))
            .ToList();
        return Ok(PagedResult<MemberResponse>.From(items, page, pageSize));
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<MemberResponse>> Add(
        Guid workspaceId, [FromBody] AddMemberRequest request, CancellationToken cancellationToken)
    {
        await _addValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await _orgUserService.GetByEmailAndOrgAsync(request.Email, _currentUser.OrgId!.Value, cancellationToken)
            ?? throw new UserNotInOrganisationException();

        if (await _memberService.IsMemberAsync(workspaceId, user.Id, cancellationToken))
        {
            throw new AlreadyMemberException();
        }

        var member = await _memberService.AddMemberAsync(workspaceId, user.Id, request.Role, _currentUser.UserId!.Value, cancellationToken);
        return Ok(new MemberResponse(member.OrgUserId, user.Email, user.Name, member.Role, member.JoinedAt, member.IsActive));
    }

    [HttpPatch("{userId:guid}/role")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> UpdateRole(
        Guid workspaceId, Guid userId, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        await _memberService.UpdateRoleAsync(workspaceId, userId, request.Role, _currentUser.UserId!.Value, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> Remove(Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        await _memberService.RemoveMemberAsync(workspaceId, userId, _currentUser.UserId!.Value, cancellationToken);
        return NoContent();
    }
}
