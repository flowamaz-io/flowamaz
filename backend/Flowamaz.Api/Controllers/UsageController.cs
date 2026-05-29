using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Plan/edition usage for a workspace (prompt 05-07) — backs the Community-edition banner and the
/// usage bars on the workflow/instance lists. Viewer can read.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/usage")]
public sealed class UsageController : ControllerBase
{
    private readonly IEditionService _edition;

    public UsageController(IEditionService edition) => _edition = edition;

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<UsageResponse>> Get(Guid workspaceId, CancellationToken cancellationToken)
    {
        var workflows = await _edition.CheckLimitAsync(workspaceId, LimitType.WorkflowCount, cancellationToken);
        var runs = await _edition.CheckLimitAsync(workspaceId, LimitType.RunsThisMonth, cancellationToken);
        var members = await _edition.CheckLimitAsync(workspaceId, LimitType.MemberCount, cancellationToken);

        return Ok(new UsageResponse(
            Edition: _edition.Edition,
            IsCommunity: _edition.IsCommunity,
            WorkflowsUsed: workflows.CurrentValue,
            WorkflowsLimit: workflows.LimitValue,
            RunsUsed: runs.CurrentValue,
            RunsLimit: runs.LimitValue,
            MembersUsed: members.CurrentValue,
            MembersLimit: members.LimitValue,
            IsAtLimit: workflows.LimitReached || runs.LimitReached || members.LimitReached));
    }
}

public sealed record UsageResponse(
    string Edition,
    bool IsCommunity,
    long WorkflowsUsed,
    long WorkflowsLimit,
    long RunsUsed,
    long RunsLimit,
    long MembersUsed,
    long MembersLimit,
    bool IsAtLimit);
