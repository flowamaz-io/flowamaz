using System.Text;
using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workflow Interpreter narratives (FUNCTIONAL.md §8.10). CEO/Auditor/Developer audiences; CEO uses
/// metered F5 AI, the others are deterministic. Download returns an audit-ready PDF by default
/// (<c>format=pdf</c>), or the Markdown source with <c>format=md</c>.
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
        Guid workspaceId, Guid id, [FromQuery] string audience = "ceo", [FromQuery] string format = "pdf",
        CancellationToken cancellationToken = default)
    {
        if (!TryParseAudience(audience, out var parsed)) return BadRequest();
        if (format is not ("pdf" or "md"))
            return BadRequest(new
            {
                success = false, statusCode = 400, code = "VALIDATION_ERROR",
                message = "Choose format=pdf (the audit-ready document) or format=md (the Markdown source).",
            });
        var narrative = await _interpreter.GenerateNarrativeAsync(workspaceId, id, parsed, cancellationToken);
        if (narrative is null) return NotFound();

        var name = $"narrative-{id}-{narrative.Audience}";
        if (format == "md") return File(Encoding.UTF8.GetBytes(narrative.Content), "text/markdown", $"{name}.md");
        var title = narrative.Audience switch
        {
            "auditor" => "Compliance record",
            "developer" => "Technical execution log",
            _ => "Run summary",
        };
        var pdf = Application.Workflow.Interpreter.NarrativePdf.Render(narrative.Content, $"{title} — run {id}", narrative.GeneratedAt);
        return File(pdf, "application/pdf", $"{name}.pdf");
    }

    private static bool TryParseAudience(string value, out NarrativeAudience audience) =>
        Enum.TryParse(value, ignoreCase: true, out audience);
}
