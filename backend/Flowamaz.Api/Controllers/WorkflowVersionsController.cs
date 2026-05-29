using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Git;
using Flowamaz.Core.Interfaces.Git;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Git-native workflow version history (FUNCTIONAL.md §2.6). History/at-commit need Viewer; the
/// diff needs Designer. A workflow that does not belong to the workspace 404s before any Git read.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/workflows/{id:guid}")]
public sealed class WorkflowVersionsController : ControllerBase
{
    private readonly WorkflowService _workflows;
    private readonly IWorkspaceGitService _git;

    public WorkflowVersionsController(WorkflowService workflows, IWorkspaceGitService git)
    {
        _workflows = workflows;
        _git = git;
    }

    [HttpGet("history")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<WorkflowCommit>>> History(
        Guid workspaceId, Guid id, CancellationToken cancellationToken)
    {
        var definition = await _workflows.GetByIdAsync(workspaceId, id, cancellationToken);
        if (definition is null) return NotFound();

        var history = await _git.GetHistoryAsync(workspaceId, id, 50, cancellationToken);
        return Ok(history);
    }

    [HttpGet("diff")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<WorkflowDiff>> Diff(
        Guid workspaceId, Guid id, [FromQuery] string from, [FromQuery] string to, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest("Both 'from' and 'to' commit SHAs are required. Pick two versions from the history list to compare them.");

        var definition = await _workflows.GetByIdAsync(workspaceId, id, cancellationToken);
        if (definition is null) return NotFound();

        var diff = await _git.GetDiffAsync(workspaceId, id, from, to, cancellationToken);
        return Ok(diff);
    }

    [HttpGet("at/{commitSha}")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<WorkflowAtCommitResponse>> AtCommit(
        Guid workspaceId, Guid id, string commitSha, CancellationToken cancellationToken)
    {
        var definition = await _workflows.GetByIdAsync(workspaceId, id, cancellationToken);
        if (definition is null) return NotFound();

        var yaml = await _git.GetWorkflowAtCommitAsync(workspaceId, id, commitSha, cancellationToken);
        return Ok(new WorkflowAtCommitResponse(id, commitSha, yaml));
    }
}
