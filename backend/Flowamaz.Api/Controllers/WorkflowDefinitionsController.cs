using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Application.Workspace.DTOs;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workflow definition CRUD + publish. Reads need Viewer, authoring and Draft delete need Designer.
/// YAML is validated before any save (invalid YAML → 422). Wrong-org workspaces 404 via the
/// authorization filter.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/workflows")]
public sealed class WorkflowDefinitionsController : ControllerBase
{
    private readonly WorkflowService _workflows;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<CreateWorkflowDefinitionRequest> _createValidator;
    private readonly IValidator<UpdateWorkflowDefinitionRequest> _updateValidator;

    public WorkflowDefinitionsController(
        WorkflowService workflows,
        ICurrentUserService currentUser,
        IValidator<CreateWorkflowDefinitionRequest> createValidator,
        IValidator<UpdateWorkflowDefinitionRequest> updateValidator)
    {
        _workflows = workflows;
        _currentUser = currentUser;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<PagedResult<WorkflowDefinitionListItem>>> List(
        Guid workspaceId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var items = await _workflows.ListAsync(workspaceId, cancellationToken);
        return Ok(PagedResult<WorkflowDefinitionListItem>.From(items, page, pageSize));
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<WorkflowDefinitionResponse>> Create(
        Guid workspaceId, [FromBody] CreateWorkflowDefinitionRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var created = await _workflows.CreateAsync(workspaceId, _currentUser.UserId!.Value, request, cancellationToken);
        return Ok(created);
    }

    [HttpGet("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<WorkflowDefinitionResponse>> Get(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var definition = await _workflows.GetByIdAsync(workspaceId, id, cancellationToken);
        return definition is null ? NotFound() : Ok(definition);
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<WorkflowDefinitionResponse>> Update(
        Guid workspaceId, Guid id, [FromBody] UpdateWorkflowDefinitionRequest request, CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var updated = await _workflows.UpdateAsync(workspaceId, id, request.YamlContent, request.SlaThresholdMs, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        // Draft-only delete: published workflows 422, missing 404 — both raised by the service.
        await _workflows.DeleteAsync(id, workspaceId, _currentUser.UserId!.Value, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/versions")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<WorkflowVersionResponse>>> Versions(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var versions = await _workflows.GetVersionsAsync(workspaceId, id, cancellationToken);
        return versions is null ? NotFound() : Ok(versions);
    }

    [HttpPost("{id:guid}/publish")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<WorkflowVersionResponse>> Publish(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var version = await _workflows.PublishAsync(workspaceId, id, _currentUser.UserId!.Value, cancellationToken);
        return version is null ? NotFound() : Ok(version);
    }
}
