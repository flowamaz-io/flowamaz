using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workspace.DTOs;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workspace CRUD + AI model config. List/create/delete are human-only (org-owner for create/delete);
/// detail/settings/ai-config are gated on workspace role. Wrong-org workspaces return 404, never 403.
/// </summary>
[ApiController]
[Route("api/v1/workspaces")]
public sealed class WorkspacesController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IWorkspaceAiConfigService _aiConfigService;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<CreateWorkspaceRequest> _createValidator;

    public WorkspacesController(
        IWorkspaceService workspaceService,
        IWorkspaceAiConfigService aiConfigService,
        ICurrentUserService currentUser,
        IValidator<CreateWorkspaceRequest> createValidator)
    {
        _workspaceService = workspaceService;
        _aiConfigService = aiConfigService;
        _currentUser = currentUser;
        _createValidator = createValidator;
    }

    [HttpGet]
    [Authorize]
    public ActionResult<PagedResult<WorkspaceListItem>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        // Membership snapshot from the JWT — the user's active workspaces at sign-in.
        var items = _currentUser.WorkspaceMemberships
            .Select(m => new WorkspaceListItem(m.WorkspaceId, m.WorkspaceName, m.WorkspaceSlug, m.RoleName))
            .ToList();
        return Ok(PagedResult<WorkspaceListItem>.From(items, page, pageSize));
    }

    [HttpPost]
    [RequireOrgOwner]
    public async Task<ActionResult<WorkspaceResponse>> Create([FromBody] CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var workspace = await _workspaceService.CreateWorkspaceAsync(
            _currentUser.OrgId!.Value, request.Name, request.Slug, _currentUser.UserId!.Value, cancellationToken);
        return Ok(ToResponse(workspace));
    }

    [HttpGet("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<WorkspaceResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceService.GetByIdAsync(id, _currentUser.OrgId!.Value, cancellationToken);
        return workspace is null ? NotFound() : Ok(ToResponse(workspace));
    }

    [HttpPut("{id:guid}/settings")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<WorkspaceResponse>> UpdateSettings(
        Guid id, [FromBody] UpdateWorkspaceSettingsRequest request, CancellationToken cancellationToken)
    {
        var unknown = request.AllowedAiProviders.Where(p => !AiProviders.All.Contains(p)).ToList();
        if (unknown.Count > 0)
        {
            throw new ConfigViolationException(
                "AI_PROVIDER_UNKNOWN",
                $"Unknown AI provider(s): {string.Join(", ", unknown)}. Allowed: {string.Join(", ", AiProviders.All)}.");
        }

        var settings = new WorkspaceSettings
        {
            MaxConcurrentRuns = request.MaxConcurrentRuns,
            RunRetentionDays = request.RunRetentionDays,
            AiCostBudgetMonthUsd = request.AiCostBudgetMonthUsd,
            AllowedAiProviders = request.AllowedAiProviders,
            DefaultAiModelOverrides = request.DefaultAiModelOverrides,
            MarketplacePolicy = request.MarketplacePolicy,
        };
        await _workspaceService.UpdateSettingsAsync(id, settings, cancellationToken);

        var updated = await _workspaceService.GetByIdAsync(id, _currentUser.OrgId!.Value, cancellationToken);
        return Ok(ToResponse(updated!));
    }

    [HttpDelete("{id:guid}")]
    [RequireOrgOwner]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        // Org-owner is verified by the filter; confirm the workspace is in this org before deleting.
        var workspace = await _workspaceService.GetByIdAsync(id, _currentUser.OrgId!.Value, cancellationToken);
        if (workspace is null) return NotFound();

        await _workspaceService.SoftDeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/ai-config")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<AiConfigView>> GetAiConfig(Guid id, CancellationToken cancellationToken) =>
        Ok(await _aiConfigService.GetAsync(id, cancellationToken));

    [HttpPut("{id:guid}/ai-config")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<AiConfigView>> UpdateAiConfig(
        Guid id, [FromBody] UpdateAiConfigRequest request, CancellationToken cancellationToken)
    {
        var overrides = request.Overrides
            .Select(o => new FunctionOverrideInput(o.FunctionId, o.Provider, o.ModelId, o.KeySource))
            .ToList();
        await _aiConfigService.UpdateAsync(id, overrides, cancellationToken);
        return Ok(await _aiConfigService.GetAsync(id, cancellationToken));
    }

    private static WorkspaceResponse ToResponse(Core.Entities.Workspaces.Workspace w) =>
        new(w.Id, w.OrgId, w.Name, w.Slug, w.Settings, w.CreatedAt);
}
