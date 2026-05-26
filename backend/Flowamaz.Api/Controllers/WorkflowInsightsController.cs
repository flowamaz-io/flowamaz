using Flowamaz.Api.Authorization;
using Flowamaz.Application.Analytics;
using Flowamaz.Application.Workspace.DTOs;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Process-Intelligence insights — list (filterable) + acknowledge. Operator+.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/insights")]
public sealed class WorkflowInsightsController : ControllerBase
{
    private readonly InsightService _insights;
    private readonly ICurrentUserService _currentUser;

    public WorkflowInsightsController(InsightService insights, ICurrentUserService currentUser)
    {
        _insights = insights;
        _currentUser = currentUser;
    }

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<PagedResult<InsightResponse>>> List(
        Guid workspaceId,
        [FromQuery] string? severity,
        [FromQuery] bool? acknowledged,
        [FromQuery] Guid? workflowDefinitionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        InsightSeverity? severityFilter = Enum.TryParse<InsightSeverity>(severity, ignoreCase: true, out var s) ? s : null;
        var items = await _insights.ListAsync(workspaceId, severityFilter, acknowledged, workflowDefinitionId, cancellationToken);
        return Ok(PagedResult<InsightResponse>.From(items, page, pageSize));
    }

    [HttpPost("{id:guid}/acknowledge")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<IActionResult> Acknowledge(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var ok = await _insights.AcknowledgeAsync(workspaceId, id, _currentUser.UserId!.Value, cancellationToken);
        return ok ? Ok(new { acknowledged = true }) : NotFound();
    }
}
