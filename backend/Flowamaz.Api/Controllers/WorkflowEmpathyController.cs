using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/workflows/{id:guid}/empathy")]
public sealed class WorkflowEmpathyController : ControllerBase
{
    private readonly IWorkflowEmpathyService _empathy;
    private readonly ILogger<WorkflowEmpathyController> _log;

    public WorkflowEmpathyController(IWorkflowEmpathyService empathy, ILogger<WorkflowEmpathyController> log)
    {
        _empathy = empathy;
        _log = log;
    }

    /// <summary>Returns the empathy analysis for a workflow definition.</summary>
    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<EmpathyAnalysis>> GetEmpathyAnalysis(
        Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        _log.LogInformation(
            "WorkflowEmpathyController.GetEmpathyAnalysis entry workspaceId={WorkspaceId} id={Id}",
            workspaceId, id);

        var result = await _empathy.AnalyseAsync(id, workspaceId, cancellationToken);

        _log.LogInformation(
            "WorkflowEmpathyController.GetEmpathyAnalysis exit score={Score}",
            result.Score);

        return Ok(result);
    }
}
