using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workflow template gallery. Browse/preview are public (no auth); install and publish are
/// workspace-scoped and require the Designer role.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class TemplatesController : ControllerBase
{
    private readonly ITemplateService _templates;
    private readonly ICurrentUserService _currentUser;

    public TemplatesController(ITemplateService templates, ICurrentUserService currentUser)
    {
        _templates = templates;
        _currentUser = currentUser;
    }

    [HttpGet("templates")]
    [AllowAnonymous]
    public async Task<ActionResult<TemplatePage>> List(
        [FromQuery] string? category, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12, CancellationToken ct = default) =>
        Ok(await _templates.ListTemplatesAsync(category, search, page, pageSize, ct));

    [HttpGet("templates/{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<TemplateDetail>> Get(Guid id, CancellationToken ct)
    {
        var template = await _templates.GetTemplateAsync(id, ct);
        return template is null
            ? NotFound(new { message = $"Template '{id}' was not found or is no longer available. Browse the gallery for current templates." })
            : Ok(template);
    }

    [HttpPost("workspaces/{workspaceId:guid}/templates/{templateId:guid}/install")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<InstallTemplateResponse>> Install(
        Guid workspaceId, Guid templateId, [FromBody] InstallTemplateRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("A signed-in user is required to install a template.");
        var workflowId = await _templates.InstallTemplateAsync(templateId, workspaceId, userId, request.Name, ct);
        return Ok(new InstallTemplateResponse(workflowId));
    }

    [HttpPost("workspaces/{workspaceId:guid}/templates/publish")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<PublishTemplateResult>> Publish(
        Guid workspaceId, [FromBody] PublishTemplateRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("A signed-in user is required to publish a template.");
        var orgId = _currentUser.OrgId
            ?? throw new InvalidOperationException("An organisation context is required to publish a template.");

        var result = await _templates.PublishTemplateAsync(
            request.WorkflowId, workspaceId, userId, orgId,
            new PublishTemplateDetails(request.Name, request.Description, request.Category, request.Tags ?? [], request.PreviewImageUrl),
            ct);
        return Ok(result);
    }
}

public sealed record InstallTemplateRequest(string Name);

public sealed record InstallTemplateResponse(Guid WorkflowId);

public sealed record PublishTemplateRequest(
    Guid WorkflowId,
    string Name,
    string Description,
    string Category,
    string[]? Tags,
    string? PreviewImageUrl);
