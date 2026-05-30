using System.Text.Json;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers.Public;

/// <summary>Public API: read instance status, stream events (SSE), and cancel a run.</summary>
[Route("api/public/v1/instances")]
public sealed class PublicInstancesController : PublicApiController
{
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowEventRepository _events;
    private readonly IWorkflowOrchestrator _orchestrator;

    public PublicInstancesController(
        IWorkflowInstanceRepository instances,
        IWorkflowEventRepository events,
        IWorkflowOrchestrator orchestrator,
        ICurrentUserService currentUser,
        IPublicApiRateLimiter rateLimiter)
        : base(currentUser, rateLimiter)
    {
        _instances = instances;
        _events = events;
        _orchestrator = orchestrator;
    }

    /// <summary>Public API: returns the status and result of a single workflow instance in the workspace. Authenticated by workspace API key and rate limited.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (await GuardAsync("read", DefaultHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var instance = await _instances.GetByIdForWorkspaceAsync(id, ws, ct);
        return instance is null ? NotFoundInstance(id) : Data(PublicInstance.From(instance));
    }

    /// <summary>Public API: streams the instance's execution events as Server-Sent Events. Authenticated by workspace API key and rate limited.</summary>
    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken ct)
    {
        if (await GuardAsync("read", DefaultHourlyLimit, ct) is { } blocked) return blocked;
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

    /// <summary>Public API: cancels a running workflow instance in the workspace. Authenticated by workspace API key and rate limited.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (await GuardAsync("trigger", TriggerHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var instance = await _instances.GetByIdForWorkspaceAsync(id, ws, ct);
        if (instance is null) return NotFoundInstance(id);

        await _orchestrator.CancelAsync(id, Guid.Empty, ct);
        return Data(new { InstanceId = id, Status = "cancelled" });
    }

    private IActionResult NotFoundInstance(Guid id) =>
        ApiError(StatusCodes.Status404NotFound, "instance_not_found",
            $"No instance found with id '{id}' in this workspace.", "errors#instance_not_found");
}
