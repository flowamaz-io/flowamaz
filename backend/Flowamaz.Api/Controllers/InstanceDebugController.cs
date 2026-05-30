using Flowamaz.Api.Authorization;
using Flowamaz.Api.Filters;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Advanced instance monitoring (prompt 07-04): replay a terminal instance as a test run, plus
/// Development-only breakpoint/resume/step controls.
///
/// Replay lives under the workspace route and needs Designer. The breakpoint/resume/step endpoints
/// live under <c>/api/v1/dev/...</c> and are HARD-gated to the Development environment — every dev
/// action returns 404 outside Development so the surface does not exist in staging/production.
/// Breakpoints are held in-memory (<see cref="IBreakpointRegistry"/>); resume/step reuse the
/// existing Redis-backed <see cref="IStepDebuggerService"/> from prompt 02-05.
/// </summary>
[ApiController]
public sealed class InstanceDebugController : ControllerBase
{
    private readonly IReplayService _replay;
    private readonly IBreakpointRegistry _breakpoints;
    private readonly IStepDebuggerService _debugger;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkspaceMemberRepository _members;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<ReplayRequest> _replayValidator;
    private readonly IValidator<SetBreakpointRequest> _breakpointValidator;
    private readonly IHostEnvironment _environment;

    public InstanceDebugController(
        IReplayService replay,
        IBreakpointRegistry breakpoints,
        IStepDebuggerService debugger,
        IWorkflowInstanceRepository instances,
        IWorkspaceMemberRepository members,
        ICurrentUserService currentUser,
        IValidator<ReplayRequest> replayValidator,
        IValidator<SetBreakpointRequest> breakpointValidator,
        IHostEnvironment environment)
    {
        _replay = replay;
        _breakpoints = breakpoints;
        _debugger = debugger;
        _instances = instances;
        _members = members;
        _currentUser = currentUser;
        _replayValidator = replayValidator;
        _breakpointValidator = breakpointValidator;
        _environment = environment;
    }

    // ── Replay (workspace-scoped, Designer) ──────────────────────────────────

    [HttpPost("api/v1/workspaces/{workspaceId:guid}/instances/{instanceId:guid}/replay")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<IActionResult> Replay(
        Guid workspaceId, Guid instanceId, [FromBody] ReplayRequest? request, CancellationToken cancellationToken)
    {
        if (request is not null)
            await _replayValidator.ValidateAndThrowAsync(request, cancellationToken);
        var newInstanceId = await _replay.ReplayInstanceAsync(
            instanceId, request?.PayloadOverride, workspaceId, cancellationToken);
        return newInstanceId is null
            ? NotFound(new
            {
                error = "instance_not_found",
                message = "Instance not found in this workspace, or it is still running. Only terminal instances can be replayed."
            })
            : Ok(new { instanceId = newInstanceId.Value, isTest = true });
    }

    // ── Breakpoints (Development only, in-memory) ────────────────────────────

    [HttpPost("api/v1/dev/breakpoints")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public async Task<IActionResult> SetBreakpoint([FromBody] SetBreakpointRequest request, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        await _breakpointValidator.ValidateAndThrowAsync(request, cancellationToken);
        if (await NotAMemberAsync(request.WorkspaceId, cancellationToken)) return Forbid();
        var bp = _breakpoints.Add(request.WorkspaceId, request.WorkflowId, request.NodeId);
        return Ok(bp);
    }

    [HttpGet("api/v1/dev/breakpoints")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public async Task<IActionResult> ListBreakpoints([FromQuery] Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        if (await NotAMemberAsync(workspaceId, cancellationToken)) return Forbid();
        return Ok(_breakpoints.List(workspaceId));
    }

    [HttpDelete("api/v1/dev/breakpoints/{id:guid}")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public async Task<IActionResult> RemoveBreakpoint(Guid id, [FromQuery] Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        if (await NotAMemberAsync(workspaceId, cancellationToken)) return Forbid();
        return _breakpoints.Remove(id) ? Ok(new { removed = true }) : NotFound();
    }

    [HttpPost("api/v1/dev/instances/{id:guid}/resume")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public async Task<IActionResult> Resume(Guid id, [FromQuery] Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        if (await NotAMemberAsync(workspaceId, cancellationToken)) return Forbid();

        // Clear this execution's breakpoint pause so the same node does not re-pause on resume.
        var instance = await _instances.GetByIdForWorkspaceAsync(id, workspaceId, cancellationToken);
        if (instance?.CurrentNodeId is { } nodeId)
            _breakpoints.MarkResumed(id, nodeId);

        var ok = await _debugger.ResumeAsync(workspaceId, id, cancellationToken);
        return ok ? Ok(new { resumed = true }) : NotFound();
    }

    [HttpPost("api/v1/dev/instances/{id:guid}/step")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public async Task<IActionResult> Step(Guid id, [FromQuery] Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        if (await NotAMemberAsync(workspaceId, cancellationToken)) return Forbid();

        var instance = await _instances.GetByIdForWorkspaceAsync(id, workspaceId, cancellationToken);
        if (instance?.CurrentNodeId is { } nodeId)
            _breakpoints.MarkResumed(id, nodeId);

        var ok = await _debugger.StepForwardAsync(workspaceId, id, cancellationToken);
        return ok ? Ok(new { stepped = true }) : NotFound();
    }

    /// <summary>True when the signed-in user is not a member of the workspace (→ 403).</summary>
    private async Task<bool> NotAMemberAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return true;
        var member = await _members.GetAsync(workspaceId, userId, cancellationToken);
        return member is null;
    }
}

public sealed record ReplayRequest(string? PayloadOverride);

public sealed record SetBreakpointRequest(Guid WorkspaceId, Guid WorkflowId, string NodeId);
