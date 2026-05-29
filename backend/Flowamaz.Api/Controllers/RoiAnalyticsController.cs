using Flowamaz.Api.Authorization;
using Flowamaz.Application.Analytics;
using Flowamaz.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// ROI analytics (prompt 05-04). Reads need Operator; configuring the financial baseline needs Admin.
/// All figures are deterministic — admin-set baselines × actual run counts.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}")]
public sealed class RoiAnalyticsController : ControllerBase
{
    private readonly IRoiAnalyticsService _roi;

    public RoiAnalyticsController(IRoiAnalyticsService roi) => _roi = roi;

    [HttpGet("analytics/roi")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<WorkspaceRoiSummary>> WorkspaceRoi(
        Guid workspaceId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        var (start, end) = ResolvePeriod(from, to);
        var summary = await _roi.GetWorkspaceRoiAsync(workspaceId, start, end, cancellationToken);
        return Ok(summary);
    }

    [HttpGet("workflows/{id:guid}/roi")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<WorkflowRoiDetail>> WorkflowRoi(
        Guid workspaceId, Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        var (start, end) = ResolvePeriod(from, to);
        var detail = await _roi.GetWorkflowRoiAsync(workspaceId, id, start, end, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("workflows/{id:guid}/roi-config")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<RoiConfigDto>> GetRoiConfig(Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var config = await _roi.GetConfigAsync(workspaceId, id, cancellationToken);
        return config is null ? NotFound() : Ok(config);
    }

    [HttpPut("workflows/{id:guid}/roi-config")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<RoiConfigDto>> PutRoiConfig(
        Guid workspaceId, Guid id, [FromBody] RoiConfigDto config, CancellationToken cancellationToken)
    {
        var updated = await _roi.UpsertConfigAsync(workspaceId, id, config, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Defaults to the current calendar month (UTC) when no range is supplied.</summary>
    private static (DateTime Start, DateTime End) ResolvePeriod(DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var start = from?.ToUniversalTime() ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = to?.ToUniversalTime() ?? now;
        return (start, end);
    }
}
