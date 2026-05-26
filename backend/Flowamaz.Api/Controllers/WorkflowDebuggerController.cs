using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Step debugger for Dev/Staging runs (FUNCTIONAL.md §8.10). All endpoints require WorkspaceAdmin;
/// the Dev/Staging-only rule is enforced in the service (Production → 403).
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/instances/{id:guid}/debug")]
public sealed class WorkflowDebuggerController : ControllerBase
{
    private readonly IStepDebuggerService _debugger;

    public WorkflowDebuggerController(IStepDebuggerService debugger) => _debugger = debugger;

    [HttpPost("pause")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> Pause(Guid workspaceId, Guid id, [FromBody] PauseRequest request, CancellationToken cancellationToken)
    {
        var ok = await _debugger.PauseAsync(workspaceId, id, request.AfterNodeId, cancellationToken);
        return ok ? Ok(new { paused = true }) : NotFound();
    }

    [HttpGet("inspect")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<DebugSnapshot>> Inspect(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await _debugger.InspectAsync(workspaceId, id, cancellationToken);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    [HttpPost("force-variable")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> ForceVariable(Guid workspaceId, Guid id, [FromBody] ForceVariableRequest request, CancellationToken cancellationToken)
    {
        var ok = await _debugger.ForceVariableAsync(workspaceId, id, request.Name, request.Value, cancellationToken);
        return ok ? Ok(new { forced = true }) : NotFound();
    }

    [HttpPost("step")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> Step(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var ok = await _debugger.StepForwardAsync(workspaceId, id, cancellationToken);
        return ok ? Ok(new { stepped = true }) : NotFound();
    }

    [HttpPost("force-branch")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> ForceBranch(Guid workspaceId, Guid id, [FromBody] ForceBranchRequest request, CancellationToken cancellationToken)
    {
        var ok = await _debugger.ForceBranchAsync(workspaceId, id, request.RouterNodeId, request.TargetBranchEdgeId, cancellationToken);
        return ok ? Ok(new { forced = true }) : NotFound();
    }

    [HttpPost("resume")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> Resume(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var ok = await _debugger.ResumeAsync(workspaceId, id, cancellationToken);
        return ok ? Ok(new { resumed = true }) : NotFound();
    }
}
