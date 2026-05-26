using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Application.Workspace.DTOs;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workflow instance management: list/trigger/detail/cancel/retry plus event, variable (sensitive
/// values masked) and timeline reads. Most actions need Operator; the read-only timeline needs
/// Viewer. Triggering is idempotent on idempotency_key.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/instances")]
public sealed class WorkflowInstancesController : ControllerBase
{
    private readonly InstanceService _instances;
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<TriggerInstanceRequest> _triggerValidator;

    public WorkflowInstancesController(
        InstanceService instances,
        IWorkflowOrchestrator orchestrator,
        ICurrentUserService currentUser,
        IValidator<TriggerInstanceRequest> triggerValidator)
    {
        _instances = instances;
        _orchestrator = orchestrator;
        _currentUser = currentUser;
        _triggerValidator = triggerValidator;
    }

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<PagedResult<InstanceListItem>>> List(
        Guid workspaceId,
        [FromQuery] string? status,
        [FromQuery] Guid? workflowDefinitionId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        InstanceStatus? statusFilter = Enum.TryParse<InstanceStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
        var items = await _instances.ListAsync(workspaceId, statusFilter, workflowDefinitionId, from, to, cancellationToken);
        return Ok(PagedResult<InstanceListItem>.From(items, page, pageSize));
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<TriggerInstanceResponse>> Trigger(
        Guid workspaceId, [FromBody] TriggerInstanceRequest request, CancellationToken cancellationToken)
    {
        await _triggerValidator.ValidateAndThrowAsync(request, cancellationToken);
        var instance = await _orchestrator.TriggerAsync(
            workspaceId, request.WorkflowDefinitionId, request.Payload, request.IdempotencyKey,
            cancellationToken: cancellationToken);
        return Ok(new TriggerInstanceResponse(instance.Id, instance.Status.ToString(), instance.CreatedAt));
    }

    [HttpGet("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<InstanceDetailResponse>> Get(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var detail = await _instances.GetDetailAsync(workspaceId, id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<InstanceDetailResponse>> Cancel(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var detail = await _instances.GetDetailAsync(workspaceId, id, cancellationToken);
        if (detail is null) return NotFound();

        await _orchestrator.CancelAsync(id, _currentUser.UserId!.Value, cancellationToken);
        return Ok(await _instances.GetDetailAsync(workspaceId, id, cancellationToken));
    }

    [HttpPost("{id:guid}/retry")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<TriggerInstanceResponse>> Retry(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var retried = await _instances.RetryAsync(workspaceId, id, cancellationToken);
        return retried is null ? NotFound() : Ok(retried);
    }

    [HttpGet("{id:guid}/events")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<PagedResult<EventResponse>>> Events(
        Guid workspaceId, Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var events = await _instances.GetEventsAsync(workspaceId, id, cancellationToken);
        return events is null ? NotFound() : Ok(PagedResult<EventResponse>.From(events, page, pageSize));
    }

    [HttpGet("{id:guid}/variables")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<IReadOnlyList<VariableResponse>>> Variables(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var variables = await _instances.GetVariablesAsync(workspaceId, id, cancellationToken);
        return variables is null ? NotFound() : Ok(variables);
    }

    [HttpGet("{id:guid}/timeline")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<TimelineEntry>>> Timeline(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var timeline = await _instances.GetTimelineAsync(workspaceId, id, cancellationToken);
        return timeline is null ? NotFound() : Ok(timeline);
    }
}
