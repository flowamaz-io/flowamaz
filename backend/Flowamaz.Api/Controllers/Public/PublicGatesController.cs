using Flowamaz.Core.Enums;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Interfaces.Services;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers.Public;

/// <summary>Public API: list pending human gates and submit decisions.</summary>
[Route("api/public/v1/gates")]
public sealed class PublicGatesController : PublicApiController
{
    private readonly GateService _gates;
    private readonly IValidator<PublicGateDecisionRequest> _decisionValidator;

    public PublicGatesController(
        GateService gates, IValidator<PublicGateDecisionRequest> decisionValidator,
        ICurrentUserService currentUser, IPublicApiRateLimiter rateLimiter)
        : base(currentUser, rateLimiter)
    {
        _gates = gates;
        _decisionValidator = decisionValidator;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        if (await GuardAsync(ApiKeyScope.InstancesRead, "read", DefaultHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        // Production approvals only — test-run gates never reach API integrations (O2).
        var gates = await _gates.ListPendingAsync(ws, includeTest: false, ct);
        return Data(gates);
    }

    [HttpPost("{id:guid}/decide")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] PublicGateDecisionRequest request, CancellationToken ct)
    {
        if (await GuardAsync(ApiKeyScope.InstancesWrite, "trigger", TriggerHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var validation = await _decisionValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ApiError(StatusCodes.Status400BadRequest, "validation_error",
                string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()), "errors#validation_error");

        var info = await _gates.GetByGateIdAsync(id, ct);
        if (info is null || info.WorkspaceId != ws)
            return ApiError(StatusCodes.Status404NotFound, "gate_not_found",
                $"No pending gate found with id '{id}' in this workspace.", "errors#gate_not_found");

        // Guid.Empty: no Flowamaz user — the audit row names the API key, plus decided_by when the caller sends it.
        var result = await _gates.DecideAsync(
            ws, info.InstanceId, info.NodeId, request.Decision!.Trim(), request.Note, Guid.Empty, ct,
            decidedByLabel: request.DecidedBy);
        return result is null
            ? ApiError(StatusCodes.Status404NotFound, "gate_not_found", "Gate could not be decided.", "errors#gate_not_found")
            : Data(result);
    }
}

/// <summary>
/// Body for a public gate decision. <c>decision</c> is "approved" or "rejected". <c>decided_by</c> (optional)
/// names the person who decided in the calling application, e.g. "Aisha Rahman (Branch Manager)"; it is
/// stored on the gate and shown in the run timeline and the audit log next to the API key.
/// </summary>
public sealed record PublicGateDecisionRequest(
    [property: JsonPropertyName("decision")] string? Decision,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("decided_by")] string? DecidedBy = null);
