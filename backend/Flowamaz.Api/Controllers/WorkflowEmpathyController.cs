using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workflow.Empathy;
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

    /// <summary>
    /// Analyses a workflow for empathy issues. If yamlContent is provided in the body,
    /// it is parsed directly so unsaved edits are reflected without a save first.
    /// </summary>
    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<EmpathyAnalysis>> GetEmpathyAnalysis(
        Guid workspaceId, Guid id,
        [FromBody] EmpathyRequest? request,
        CancellationToken cancellationToken)
    {
        _log.LogInformation(
            "WorkflowEmpathyController.GetEmpathyAnalysis entry workspaceId={WorkspaceId} id={Id} hasYaml={HasYaml}",
            workspaceId, id, !string.IsNullOrWhiteSpace(request?.YamlContent));

        EmpathyAnalysis result;
        if (!string.IsNullOrWhiteSpace(request?.YamlContent))
        {
            result = await _empathy.AnalyseYamlAsync(request.YamlContent, id, workspaceId, cancellationToken);
        }
        else
        {
            result = await _empathy.AnalyseAsync(id, workspaceId, cancellationToken);
        }

        _log.LogInformation(
            "WorkflowEmpathyController.GetEmpathyAnalysis exit score={Score}",
            result.Score);

        return Ok(result);
    }
}
