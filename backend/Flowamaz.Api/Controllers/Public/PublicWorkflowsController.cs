using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers.Public;

/// <summary>Public API: list/read published workflows and trigger runs by slug.</summary>
[Route("api/public/v1/workflows")]
public sealed class PublicWorkflowsController : PublicApiController
{
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowOrchestrator _orchestrator;

    public PublicWorkflowsController(
        IWorkflowDefinitionRepository definitions,
        IWorkflowInstanceRepository instances,
        IWorkflowOrchestrator orchestrator,
        ICurrentUserService currentUser,
        IPublicApiRateLimiter rateLimiter)
        : base(currentUser, rateLimiter)
    {
        _definitions = definitions;
        _instances = instances;
        _orchestrator = orchestrator;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery(Name = "per_page")] int? perPage, CancellationToken ct)
    {
        if (await GuardAsync("read", DefaultHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var published = (await _definitions.GetForWorkspaceAsync(ws, ct))
            .Where(w => w.Status == WorkflowStatus.Published)
            .Select(PublicWorkflow.From)
            .ToList();

        var (p, size) = ReadPaging(page, perPage);
        var items = published.Skip((p - 1) * size).Take(size).ToList();
        return Data(items, PageMeta(p, size, published.Count));
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        if (await GuardAsync("read", DefaultHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var definition = await _definitions.GetBySlugForWorkspaceAsync(ws, slug, ct);
        if (definition is null || definition.Status != WorkflowStatus.Published)
            return NotFoundSlug(slug);

        return Data(PublicWorkflow.From(definition));
    }

    [HttpPost("{slug}/trigger")]
    public async Task<IActionResult> Trigger(string slug, CancellationToken ct)
    {
        if (await GuardAsync("trigger", TriggerHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var definition = await _definitions.GetBySlugForWorkspaceAsync(ws, slug, ct);
        if (definition is null || definition.Status != WorkflowStatus.Published)
            return NotFoundSlug(slug);

        var payload = await ReadRawBodyAsync(ct);
        var idempotencyKey = Request.Headers.TryGetValue("X-Idempotency-Key", out var k) ? k.ToString() : null;

        var instance = await _orchestrator.TriggerAsync(
            ws, definition.Id, payload, idempotencyKey, InstanceTriggerType.Manual,
            correlationId: null, isTest: false, cancellationToken: ct);

        return Data(new { InstanceId = instance.Id, Status = "accepted" }, statusCode: StatusCodes.Status202Accepted);
    }

    [HttpGet("{slug}/instances")]
    public async Task<IActionResult> Instances(
        string slug, [FromQuery] int? page, [FromQuery(Name = "per_page")] int? perPage, CancellationToken ct)
    {
        if (await GuardAsync("read", DefaultHourlyLimit, ct) is { } blocked) return blocked;
        var ws = WorkspaceId!.Value;

        var definition = await _definitions.GetBySlugForWorkspaceAsync(ws, slug, ct);
        if (definition is null)
            return NotFoundSlug(slug);

        var all = (await _instances.GetForWorkspaceAsync(ws, includeTest: false, ct))
            .Where(i => i.WorkflowDefinitionId == definition.Id)
            .Select(PublicInstance.From)
            .ToList();

        var (p, size) = ReadPaging(page, perPage);
        var items = all.Skip((p - 1) * size).Take(size).ToList();
        return Data(items, PageMeta(p, size, all.Count));
    }

    private IActionResult NotFoundSlug(string slug) =>
        ApiError(StatusCodes.Status404NotFound, "workflow_not_found",
            $"No published workflow found with slug '{slug}'.", "errors#workflow_not_found");
}
