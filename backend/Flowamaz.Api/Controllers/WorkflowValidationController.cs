using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/workflows")]
public sealed class WorkflowValidationController : ControllerBase
{
    private readonly IWorkflowValidator _validator;
    private readonly ILogger<WorkflowValidationController> _log;

    public WorkflowValidationController(IWorkflowValidator validator, ILogger<WorkflowValidationController> log)
    {
        _validator = validator;
        _log = log;
    }

    /// <summary>
    /// Validate workflow YAML against all 6 layers. Always returns 200 — validation
    /// errors are in the response body (IsValid field), not HTTP status codes.
    /// </summary>
    [HttpPost("validate")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<ValidationResult>> Validate(
        Guid workspaceId, [FromBody] ValidateWorkflowRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("[WorkflowValidationController] Validate entry workspaceId={WorkspaceId}", workspaceId);

        if (string.IsNullOrWhiteSpace(request.YamlContent))
        {
            _log.LogWarning("[WorkflowValidationController] Validate empty yaml");
            return Ok(new ValidationResult(false,
                [new ValidationIssue(1, "SCH-000", "yaml_content is required. Provide the workflow YAML to validate.", null, null, null)],
                [], []));
        }

        var result = await _validator.ValidateAsync(request.YamlContent, workspaceId, cancellationToken);
        _log.LogInformation("[WorkflowValidationController] Validate exit isValid={IsValid} errors={E}",
            result.IsValid, result.Errors.Count);
        return Ok(result);
    }
}

public sealed record ValidateWorkflowRequest(string YamlContent);
