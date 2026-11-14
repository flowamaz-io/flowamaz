using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workflow.Twin;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workflow Twin (FUNCTIONAL.md §8.1): the latest shadow-simulation result of a workflow, running it on demand, and the
/// per-workflow badges for the workflow list. Simulations never run anything for real and make no AI calls.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}")]
public sealed class WorkflowTwinController : ControllerBase
{
    private readonly WorkflowTwinService _twin;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<WorkflowTwinController> _logger;

    public WorkflowTwinController(WorkflowTwinService twin, ICurrentUserService currentUser, ILogger<WorkflowTwinController> logger)
    {
        _twin = twin;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>The latest Twin result for a workflow (run is null before the first simulation).</summary>
    [HttpGet("workflows/{id:guid}/twin")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<TwinLatestDto>> Latest(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("WorkflowTwinController.Latest enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, id);
        // Only people who can see every run's data (Operator and up, or the org owner) see values taken from real payloads.
        var canSeeRunData = _currentUser.IsOrgOwner || _currentUser.GetWorkspaceRole(workspaceId) >= WorkspaceRole.Operator;
        return Ok(await _twin.GetLatestAsync(workspaceId, id, canSeeRunData, cancellationToken));
    }

    /// <summary>Simulates the workflow now. Designers and above; it takes about a second.</summary>
    [HttpPost("workflows/{id:guid}/twin")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<TwinRunDto>> Run(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("WorkflowTwinController.Run enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, id);
        var run = await _twin.RunAsync(workspaceId, id, "manual", _currentUser.UserId, cancellationToken);
        return Ok(run);
    }

    /// <summary>The latest Twin result of every workflow in the workspace, for the badges on the workflow list.</summary>
    [HttpGet("twin/summary")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<TwinSummaryDto>>> Summary(Guid workspaceId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("WorkflowTwinController.Summary enter workspace={WorkspaceId}", workspaceId);
        return Ok(await _twin.GetSummaryAsync(workspaceId, cancellationToken));
    }
}
