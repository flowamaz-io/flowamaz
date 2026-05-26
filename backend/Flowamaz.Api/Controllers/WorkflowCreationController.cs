using System.Text;
using System.Text.Json;
using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/workflows")]
public sealed class WorkflowCreationController : ControllerBase
{
    private readonly INlYamlGenerationService _generator;
    private readonly ICopilotService _copilot;
    private readonly IVisualInputService _visualInput;
    private readonly IConversationImportService _conversationImport;
    private readonly ISopParsingService _sopParsing;
    private readonly ILogger<WorkflowCreationController> _log;

    private const long MaxImageBytes = 10 * 1024 * 1024; // 10MB
    private const long MaxDocBytes = 20 * 1024 * 1024; // 20MB
    private static readonly HashSet<string> AllowedMimeTypes = ["image/jpeg", "image/png", "image/heic", "application/pdf"];
    private static readonly HashSet<string> AllowedDocMimeTypes =
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/plain"
    ];
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public WorkflowCreationController(
        INlYamlGenerationService generator,
        ICopilotService copilot,
        IVisualInputService visualInput,
        IConversationImportService conversationImport,
        ISopParsingService sopParsing,
        ILogger<WorkflowCreationController> log)
    {
        _generator = generator;
        _copilot = copilot;
        _visualInput = visualInput;
        _conversationImport = conversationImport;
        _sopParsing = sopParsing;
        _log = log;
    }

    /// <summary>
    /// Generate workflow YAML from a 6-section natural language description.
    /// Returns SSE stream: lines of YAML content, then a final data:done JSON envelope.
    /// </summary>
    [HttpPost("generate")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task Generate(Guid workspaceId, [FromBody] NlWorkflowRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.Generate entry workspaceId={WorkspaceId}", workspaceId);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            var result = await _generator.GenerateAsync(request, workspaceId, cancellationToken);

            // Stream the YAML line-by-line as SSE events
            foreach (var line in result.YamlContent.Split('\n'))
            {
                var data = $"data: {JsonSerializer.Serialize(new { type = "chunk", content = line })}\n\n";
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(data), cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            // Final envelope
            var done = new
            {
                type = "done",
                yaml_content = result.YamlContent,
                validation_result = result.ValidationResult,
                tokens_used = result.TokensUsed,
                cached = result.Cached
            };
            var doneData = $"data: {JsonSerializer.Serialize(done, JsonOpts)}\n\n";
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(doneData), cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WorkflowCreationController.Generate error workspaceId={WorkspaceId}", workspaceId);
            var error = $"data: {JsonSerializer.Serialize(new { type = "error", message = "Generation failed. Check your input and try again." })}\n\n";
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(error), cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        _log.LogInformation("WorkflowCreationController.Generate exit workspaceId={WorkspaceId}", workspaceId);
    }

    /// <summary>
    /// Co-pilot command endpoint. Full pipeline: rate → budget → pattern → cache → AI(F1).
    /// Rate limited to 60 calls/user/hour.
    /// </summary>
    [HttpPost("{id:guid}/copilot")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<CopilotResponse>> Copilot(
        Guid workspaceId, Guid id, [FromBody] CopilotRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.Copilot entry workspaceId={WorkspaceId} workflowId={Id}", workspaceId, id);

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var result = await _copilot.ProcessCommandAsync(request.Command, request.YamlContent, workspaceId, userId, cancellationToken);

        if (!result.Success)
        {
            _log.LogWarning("WorkflowCreationController.Copilot rejected errorMessage={Error}", result.ErrorMessage);
            return StatusCode(429, new { error = result.ErrorMessage });
        }

        _log.LogInformation("WorkflowCreationController.Copilot exit matched={Pattern} cacheHit={Cache}", result.MatchedPattern, result.CacheHit);
        return Ok(new CopilotResponse(result.MatchedPattern, result.YamlPatch, result.CacheHit));
    }

    /// <summary>
    /// Parse a SOP document (PDF, DOCX, TXT) and generate workflow YAML.
    /// Max 20MB. Returns YAML draft plus extracted steps.
    /// </summary>
    [HttpPost("from-document")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<SopParseResult>> FromDocument(
        Guid workspaceId, IFormFile document, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.FromDocument entry workspaceId={WorkspaceId}", workspaceId);

        if (document == null || document.Length == 0)
            return BadRequest(new { error = "No document provided. Upload a PDF, DOCX, or TXT file up to 20MB." });

        if (document.Length > MaxDocBytes)
            return BadRequest(new { error = "Document too large. Maximum size is 20MB. Reduce the file size and try again." });

        var mimeType = document.ContentType.ToLowerInvariant();
        if (!AllowedDocMimeTypes.Contains(mimeType))
            return BadRequest(new { error = $"Unsupported file type '{mimeType}'. Upload a PDF, DOCX, or plain text file." });

        using var ms = new MemoryStream();
        await document.CopyToAsync(ms, cancellationToken);
        var result = await _sopParsing.ParseAsync(ms.ToArray(), mimeType, workspaceId, cancellationToken);

        _log.LogInformation("WorkflowCreationController.FromDocument exit steps={Steps} tokens={Tokens}", result.ExtractedSteps.Count, result.TokensUsed);
        return Ok(result);
    }

    /// <summary>
    /// Upload a workflow diagram image (whiteboard, sketch, photo) — max 10MB.
    /// Returns a YAML draft plus any low-confidence elements for user review.
    /// </summary>
    [HttpPost("from-image")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<VisualInputResult>> FromImage(Guid workspaceId, IFormFile image, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.FromImage entry workspaceId={WorkspaceId}", workspaceId);

        if (image == null || image.Length == 0)
            return BadRequest(new { error = "No image provided. Upload a JPG, PNG, HEIC or PDF file up to 10MB." });

        if (image.Length > MaxImageBytes)
            return BadRequest(new { error = "Image too large. Maximum size is 10MB. Resize the image and try again." });

        var mimeType = image.ContentType.ToLowerInvariant();
        if (!AllowedMimeTypes.Contains(mimeType))
            return BadRequest(new { error = $"Unsupported file type '{mimeType}'. Upload a JPG, PNG, HEIC or PDF." });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, cancellationToken);
        var result = await _visualInput.ProcessImageAsync(ms.ToArray(), mimeType, workspaceId, cancellationToken);

        _log.LogInformation("WorkflowCreationController.FromImage exit lowConf={Low}", result.LowConfidenceElements.Count);
        return Ok(result);
    }

    /// <summary>
    /// Import a workflow from a conversation thread (Slack, Teams, email, or plain text).
    /// Extracts process steps using Claude then generates YAML via the NL pipeline.
    /// </summary>
    [HttpPost("from-conversation")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<ConversationImportResult>> FromConversation(
        Guid workspaceId, [FromBody] ConversationImportRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.FromConversation entry workspaceId={WorkspaceId} source={Source}", workspaceId, request.SourceType);

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "No conversation text provided. Paste a Slack thread, email chain, or meeting notes." });

        var validSources = new[] { "slack", "teams", "email", "generic" };
        if (!validSources.Contains(request.SourceType.ToLowerInvariant()))
            return BadRequest(new { error = $"Unknown source type '{request.SourceType}'. Use slack, teams, email, or generic." });

        var result = await _conversationImport.ImportAsync(request.Text, request.SourceType, workspaceId, cancellationToken);

        _log.LogInformation("WorkflowCreationController.FromConversation exit confidence={Confidence}", result.ConfidenceScore);
        return Ok(result);
    }
}

public sealed record CopilotRequest(string Command, string? YamlContent);
public sealed record CopilotResponse(string? MatchedPattern, string? YamlPatch, bool CacheHit);
public sealed record ConversationImportRequest(string Text, string SourceType);
