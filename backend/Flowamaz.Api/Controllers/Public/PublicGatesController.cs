using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers.Public;

/// <summary>Public API: list pending human gates and submit decisions.</summary>
[Route("api/public/v1/gates")]
public sealed class PublicGatesController : PublicApiController
{
    private readonly GateService _gates;

    public PublicGatesController(GateService gates, ICurrentUserService currentUser, IPublicApiRateLimiter rateLimiter)
        : base(currentUser, rateLimiter)
    {
        _gates = gates;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        if (await GuardAsync("read", DefaultHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var gates = await _gates.ListPendingAsync(ws, ct);
        return Data(gates);
    }

    [HttpPost("{id:guid}/decide")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] PublicGateDecisionRequest request, CancellationToken ct)
    {
        if (await GuardAsync("trigger", TriggerHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var info = await _gates.GetByGateIdAsync(id, ct);
        if (info is null || info.WorkspaceId != ws)
            return ApiError(StatusCodes.Status404NotFound, "gate_not_found",
                $"No pending gate found with id '{id}' in this workspace.", "errors#gate_not_found");

        var result = await _gates.DecideAsync(ws, info.InstanceId, info.NodeId, request.Decision, request.Note, Guid.Empty, ct);
        return result is null
            ? ApiError(StatusCodes.Status404NotFound, "gate_not_found", "Gate could not be decided.", "errors#gate_not_found")
            : Data(result);
    }
}

/// <summary>Body for a public gate decision. <c>Decision</c> is "approved" or "rejected".</summary>
public sealed record PublicGateDecisionRequest(string Decision, string? Note);
