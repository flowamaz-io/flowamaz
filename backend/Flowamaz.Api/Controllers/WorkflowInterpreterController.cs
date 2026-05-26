using System.Text;
using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workflow Interpreter narratives (FUNCTIONAL.md §8.10). CEO/Auditor/Developer audiences; CEO uses
/// metered F5 AI, the others are deterministic. Download returns the markdown as an attachment
/// (Phase 2 keeps it to markdown; PDF rendering can come later).
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/instances/{id:guid}/narrative")]
public sealed class WorkflowInterpreterController : ControllerBase
{
    private readonly IWorkflowInterpreterService _interpreter;

    public WorkflowInterpreterController(IWorkflowInterpreterService interpreter) => _interpreter = interpreter;

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<ActionResult<InterpreterNarrative>> Get(
        Guid workspaceId, Guid id, [FromQuery] string audience = "ceo", CancellationToken cancellationToken = default)
    {
        if (!TryParseAudience(audience, out var parsed)) return BadRequest();
        var narrative = await _interpreter.GenerateNarrativeAsync(workspaceId, id, parsed, cancellationToken);
        return narrative is null ? NotFound() : Ok(narrative);
    }

    [HttpPost("download")]
    [RequireWorkspaceRole(WorkspaceRole.Operator)]
    public async Task<IActionResult> Download(
        Guid workspaceId, Guid id, [FromQuery] string audience = "ceo", CancellationToken cancellationToken = default)
    {
        if (!TryParseAudience(audience, out var parsed)) return BadRequest();
        var narrative = await _interpreter.GenerateNarrativeAsync(workspaceId, id, parsed, cancellationToken);
        if (narrative is null) return NotFound();

        var bytes = Encoding.UTF8.GetBytes(narrative.Content);
        return File(bytes, "text/markdown", $"narrative-{id}-{narrative.Audience}.md");
    }

    private static bool TryParseAudience(string value, out NarrativeAudience audience) =>
        Enum.TryParse(value, ignoreCase: true, out audience);
}
