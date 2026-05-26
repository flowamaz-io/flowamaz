using Flowamaz.Api.Authorization;
using Flowamaz.Application.Analytics;
using Flowamaz.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workflow Weather executive view (FUNCTIONAL.md §11.6). Status is computed server-side. Operator+.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/weather")]
public sealed class WorkflowWeatherController : ControllerBase
{
    private readonly WorkflowWeatherService _weather;

    public WorkflowWeatherController(WorkflowWeatherService weather) => _weather = weather;

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<WeatherResponse>> Get(Guid workspaceId, CancellationToken cancellationToken) =>
        Ok(await _weather.GetWeatherAsync(workspaceId, cancellationToken));
}
