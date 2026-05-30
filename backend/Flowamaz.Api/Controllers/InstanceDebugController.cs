using Flowamaz.Api.Authorization;
using Flowamaz.Api.Filters;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
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
    private readonly IHostEnvironment _environment;

    public InstanceDebugController(
        IReplayService replay,
        IBreakpointRegistry breakpoints,
        IStepDebuggerService debugger,
        IHostEnvironment environment)
    {
        _replay = replay;
        _breakpoints = breakpoints;
        _debugger = debugger;
        _environment = environment;
    }

    // ── Replay (workspace-scoped, Designer) ──────────────────────────────────

    [HttpPost("api/v1/workspaces/{workspaceId:guid}/instances/{instanceId:guid}/replay")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<IActionResult> Replay(
        Guid workspaceId, Guid instanceId, [FromBody] ReplayRequest? request, CancellationToken cancellationToken)
    {
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
    public IActionResult SetBreakpoint([FromBody] SetBreakpointRequest request)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        var bp = _breakpoints.Add(request.WorkspaceId, request.WorkflowId, request.NodeId);
        return Ok(bp);
    }

    [HttpGet("api/v1/dev/breakpoints")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public IActionResult ListBreakpoints([FromQuery] Guid workspaceId)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        return Ok(_breakpoints.List(workspaceId));
    }

    [HttpDelete("api/v1/dev/breakpoints/{id:guid}")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public IActionResult RemoveBreakpoint(Guid id)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        return _breakpoints.Remove(id) ? Ok(new { removed = true }) : NotFound();
    }

    [HttpPost("api/v1/dev/instances/{id:guid}/resume")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public async Task<IActionResult> Resume(Guid id, [FromQuery] Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        var ok = await _debugger.ResumeAsync(workspaceId, id, cancellationToken);
        return ok ? Ok(new { resumed = true }) : NotFound();
    }

    [HttpPost("api/v1/dev/instances/{id:guid}/step")]
    [Authorize]
    [ServiceFilter(typeof(DevelopmentOnlyFilter))]
    public async Task<IActionResult> Step(Guid id, [FromQuery] Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return NotFound();
        var ok = await _debugger.StepForwardAsync(workspaceId, id, cancellationToken);
        return ok ? Ok(new { stepped = true }) : NotFound();
    }
}

public sealed record ReplayRequest(string? PayloadOverride);

public sealed record SetBreakpointRequest(Guid WorkspaceId, Guid WorkflowId, string NodeId);
