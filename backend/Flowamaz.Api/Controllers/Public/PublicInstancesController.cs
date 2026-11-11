using Flowamaz.Core.Enums;
using System.Text.Json;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Flowamaz.Api.Controllers.Public;

/// <summary>Public API: read instance status, stream events (SSE), and cancel a run.</summary>
[Route("api/public/v1/instances")]
public sealed class PublicInstancesController : PublicApiController
{
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowEventRepository _events;
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly IValidator<PublicCancelRequest> _cancelValidator;

    public PublicInstancesController(
        IWorkflowInstanceRepository instances,
        IWorkflowEventRepository events,
        IWorkflowOrchestrator orchestrator,
        ICurrentUserService currentUser,
        IPublicApiRateLimiter rateLimiter,
        IValidator<PublicCancelRequest> cancelValidator)
        : base(currentUser, rateLimiter)
    {
        _instances = instances;
        _events = events;
        _orchestrator = orchestrator;
        _cancelValidator = cancelValidator;
    }

    /// <summary>Public API: returns the status and result of a single workflow instance in the workspace. Authenticated by workspace API key and rate limited.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (await GuardAsync(ApiKeyScope.InstancesRead, ReadBucket, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var instance = await _instances.GetByIdForWorkspaceAsync(id, ws, ct);
        return instance is null ? NotFoundInstance(id) : Data(PublicInstance.From(instance));
    }

    /// <summary>Public API: streams the instance's execution events as Server-Sent Events. Authenticated by workspace API key and rate limited.</summary>
    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken ct)
    {
        if (await GuardAsync(ApiKeyScope.InstancesRead, ReadBucket, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var instance = await _instances.GetByIdForWorkspaceAsync(id, ws, ct);
        if (instance is null) return NotFoundInstance(id);

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        var events = await _events.GetForInstanceAsync(id, ct);
        foreach (var evt in events)
        {
            var line = $"data: {JsonSerializer.Serialize(PublicEvent.From(evt), PublicJson)}\n\n";
            await Response.WriteAsync(line, ct);
        }
        await Response.Body.FlushAsync(ct);
        return new EmptyResult();
    }

    /// <summary>
    /// Public API: cancels a running workflow instance in the workspace. Authenticated by workspace API key and rate
    /// limited. The body is optional; <c>cancelled_by</c> names who stopped the run in the calling application, and
    /// the audit log shows "&lt;cancelled_by&gt; via API key …".
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PublicCancelRequest? request, CancellationToken ct)
    {
        if (await GuardAsync(ApiKeyScope.InstancesWrite, DecideBucket, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        if (request is not null)
        {
            var validation = await _cancelValidator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return ApiError(StatusCodes.Status400BadRequest, "validation_error",
                    string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()), "errors#validation_error");
        }

        var instance = await _instances.GetByIdForWorkspaceAsync(id, ws, ct);
        if (instance is null) return NotFoundInstance(id);

        // A run that has already ended is left as it is (no-op), so report the status it really has.
        var status = instance.CanTransitionTo(InstanceStatus.Cancelled)
            ? "cancelled"
            : instance.Status.ToString().ToLowerInvariant();
        await _orchestrator.CancelAsync(id, Guid.Empty, ct, cancelledByLabel: request?.CancelledBy);
        return Data(new { InstanceId = id, Status = status });
    }

    private IActionResult NotFoundInstance(Guid id) =>
        ApiError(StatusCodes.Status404NotFound, "instance_not_found",
            $"No instance found with id '{id}' in this workspace.", "errors#instance_not_found");
}

/// <summary>Optional body for a public cancel. <c>cancelled_by</c>: who stopped the run, e.g. "Aisha Rahman (customer)".</summary>
public sealed record PublicCancelRequest(
    [property: JsonPropertyName("cancelled_by")] string? CancelledBy = null);
