using System.Text.RegularExpressions;
using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
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
    private readonly WorkflowService _workflows;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<WorkflowCreationController> _log;

    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const long MaxDocBytes = 20 * 1024 * 1024;
    private static readonly HashSet<string> AllowedMimeTypes = ["image/jpeg", "image/png", "image/heic", "application/pdf"];
    private static readonly HashSet<string> AllowedDocMimeTypes =
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/plain"
    ];
    private static readonly string[] ValidMethods = ["nl", "voice", "visual", "conversation", "document", "canvas"];
    private static readonly string[] ValidSources = ["slack", "teams", "email", "generic"];

    public WorkflowCreationController(
        INlYamlGenerationService generator,
        ICopilotService copilot,
        IVisualInputService visualInput,
        IConversationImportService conversationImport,
        ISopParsingService sopParsing,
        WorkflowService workflows,
        ICurrentUserService currentUser,
        ILogger<WorkflowCreationController> log)
    {
        _generator = generator;
        _copilot = copilot;
        _visualInput = visualInput;
        _conversationImport = conversationImport;
        _sopParsing = sopParsing;
        _workflows = workflows;
        _currentUser = currentUser;
        _log = log;
    }

    /// <summary>
    /// Unified creation endpoint — all 6 creation methods. Backend generates/parses YAML, saves the
    /// workflow definition, and returns the workflowId. Frontend navigates directly to canvas editor.
    /// </summary>
    [HttpPost("create")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<WorkflowCreationResult>> CreateWorkflow(
        Guid workspaceId, [FromBody] UnifiedWorkflowCreateRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.CreateWorkflow entry workspaceId={WorkspaceId} method={Method}", workspaceId, request.Method);

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Provide a workflow name to continue." });

        if (!ValidMethods.Contains(request.Method))
            return BadRequest(new { error = $"Unknown method '{request.Method}'. Use: nl, voice, visual, conversation, document, or canvas." });

        var slug = UniqueSlug(request.Name);
        string yamlContent;
        ValidationResult? validationResult = null;
        int tokensUsed = 0;
        bool cached = false;
        var method = request.Method;

        try
        {
            switch (method)
            {
                case "nl":
                case "voice":
                {
                    if (request.NlRequest is null)
                        return BadRequest(new { error = "Provide nlRequest fields (purpose, steps, trigger) to generate a workflow." });

                    var gen = await _generator.GenerateAsync(request.NlRequest, workspaceId, cancellationToken);
                    yamlContent = gen.YamlContent;
                    validationResult = gen.ValidationResult;
                    tokensUsed = gen.TokensUsed;
                    cached = gen.Cached;
                    break;
                }

                case "visual":
                {
                    if (string.IsNullOrWhiteSpace(request.ImageBase64))
                        return BadRequest(new { error = "Provide an imageBase64 value for visual workflow creation." });

                    byte[] imageBytes;
                    try { imageBytes = Convert.FromBase64String(request.ImageBase64); }
                    catch { return BadRequest(new { error = "imageBase64 is not valid base64. Re-encode the image and try again." }); }

                    if (imageBytes.Length > MaxImageBytes)
                        return BadRequest(new { error = "Image too large. Maximum is 10MB. Resize the image and try again." });

                    var mimeType = (request.ImageMimeType ?? "image/jpeg").ToLowerInvariant();
                    if (!AllowedMimeTypes.Contains(mimeType))
                        return BadRequest(new { error = $"Unsupported image type '{mimeType}'. Use JPG, PNG, HEIC, or PDF." });

                    var vis = await _visualInput.ProcessImageAsync(imageBytes, mimeType, workspaceId, cancellationToken);
                    yamlContent = vis.YamlDraft;
                    tokensUsed = vis.TokensUsed;
                    break;
                }

                case "conversation":
                {
                    if (string.IsNullOrWhiteSpace(request.ConversationText))
                        return BadRequest(new { error = "Paste a Slack thread, email chain, or meeting notes in conversationText." });

                    var source = (request.SourceType ?? "generic").ToLowerInvariant();
                    if (!ValidSources.Contains(source))
                        return BadRequest(new { error = $"Unknown sourceType '{source}'. Use slack, teams, email, or generic." });

                    var conv = await _conversationImport.ImportAsync(request.ConversationText, source, workspaceId, cancellationToken);
                    yamlContent = conv.YamlContent;
                    tokensUsed = conv.TokensUsed;
                    break;
                }

                case "document":
                {
                    if (string.IsNullOrWhiteSpace(request.DocumentBase64))
                        return BadRequest(new { error = "Provide a documentBase64 value for document workflow creation." });

                    byte[] docBytes;
                    try { docBytes = Convert.FromBase64String(request.DocumentBase64); }
                    catch { return BadRequest(new { error = "documentBase64 is not valid base64. Re-encode the file and try again." }); }

                    if (docBytes.Length > MaxDocBytes)
                        return BadRequest(new { error = "Document too large. Maximum is 20MB. Reduce the file size and try again." });

                    var docMime = (request.DocumentMimeType ?? "application/pdf").ToLowerInvariant();
                    if (!AllowedDocMimeTypes.Contains(docMime))
                        return BadRequest(new { error = $"Unsupported document type '{docMime}'. Upload a PDF, DOCX, or TXT file." });

                    var sop = await _sopParsing.ParseAsync(docBytes, docMime, workspaceId, cancellationToken);
                    yamlContent = sop.YamlContent;
                    tokensUsed = sop.TokensUsed;
                    break;
                }

                default: // "canvas"
                    yamlContent = string.Empty;
                    break;
            }
        }
        catch (Exception ex) when (ex is not WorkflowSlugExistsException)
        {
            _log.LogError(ex, "WorkflowCreationController.CreateWorkflow AI service error method={Method}", method);
            return StatusCode(502, new { error = $"AI processing failed. {ex.Message} Please try again." });
        }

        var createdByMethod = method switch
        {
            "nl" => WorkflowCreatedByMethod.NaturalLanguage,
            "voice" => WorkflowCreatedByMethod.Voice,
            "visual" => WorkflowCreatedByMethod.VisualInput,
            "conversation" => WorkflowCreatedByMethod.Conversation,
            "document" => WorkflowCreatedByMethod.Document,
            _ => WorkflowCreatedByMethod.Canvas,
        };

        try
        {
            var definition = await _workflows.CreateAsync(workspaceId, _currentUser.UserId!.Value,
                new CreateWorkflowDefinitionRequest(request.Name, slug, yamlContent, null, createdByMethod),
                cancellationToken);

            _log.LogInformation("WorkflowCreationController.CreateWorkflow exit workflowId={WorkflowId} method={Method} tokens={Tokens}", definition.Id, method, tokensUsed);
            return Ok(new WorkflowCreationResult(definition.Id, definition.Name, definition.Slug, method, validationResult, tokensUsed, cached));
        }
        catch (WorkflowSlugExistsException)
        {
            return Conflict(new { error = $"A workflow named '{request.Name}' already exists in this workspace. Choose a different name." });
        }
    }

    /// <summary>
    /// Generate workflow YAML from a 6-section natural language description.
    /// Returns raw YAML — prefer POST /create for the full save-and-navigate flow.
    /// </summary>
    [HttpPost("generate")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<IActionResult> GenerateWorkflow(Guid workspaceId, [FromBody] NlWorkflowRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.GenerateWorkflow entry workspaceId={WorkspaceId}", workspaceId);
        var result = await _generator.GenerateAsync(request, workspaceId, cancellationToken);
        _log.LogInformation("WorkflowCreationController.GenerateWorkflow exit workspaceId={WorkspaceId} tokens={Tokens} cached={Cached}", workspaceId, result.TokensUsed, result.Cached);
        return Ok(result);
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
    /// Parse a SOP document (PDF, DOCX, TXT) and return raw YAML.
    /// Prefer POST /create with method=document for the full save-and-navigate flow.
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
    /// Upload a workflow diagram image — max 10MB. Returns raw YAML.
    /// Prefer POST /create with method=visual for the full save-and-navigate flow.
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
    /// Import a workflow from a conversation thread. Returns raw YAML.
    /// Prefer POST /create with method=conversation for the full save-and-navigate flow.
    /// </summary>
    [HttpPost("from-conversation")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<ConversationImportResult>> FromConversation(
        Guid workspaceId, [FromBody] ConversationImportRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.FromConversation entry workspaceId={WorkspaceId} source={Source}", workspaceId, request.SourceType);

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "No conversation text provided. Paste a Slack thread, email chain, or meeting notes." });

        if (!ValidSources.Contains(request.SourceType.ToLowerInvariant()))
            return BadRequest(new { error = $"Unknown source type '{request.SourceType}'. Use slack, teams, email, or generic." });

        var result = await _conversationImport.ImportAsync(request.Text, request.SourceType, workspaceId, cancellationToken);

        _log.LogInformation("WorkflowCreationController.FromConversation exit confidence={Confidence}", result.ConfidenceScore);
        return Ok(result);
    }

    private static string UniqueSlug(string name)
    {
        var base_ = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(base_)) base_ = "untitled";
        return $"{base_}-{Guid.NewGuid().ToString("N")[..6]}";
    }
}

public sealed record UnifiedWorkflowCreateRequest(
    string Name,
    string Method,
    NlWorkflowRequest? NlRequest = null,
    string? ImageBase64 = null,
    string? ImageMimeType = null,
    string? ConversationText = null,
    string? SourceType = null,
    string? DocumentBase64 = null,
    string? DocumentMimeType = null
);

public sealed record WorkflowCreationResult(
    Guid WorkflowId,
    string WorkflowName,
    string WorkflowSlug,
    string Method,
    ValidationResult? ValidationResult = null,
    int? TokensUsed = null,
    bool? Cached = null
);

public sealed record CopilotRequest(string Command, string? YamlContent);
public sealed record CopilotResponse(string? MatchedPattern, string? YamlPatch, bool CacheHit);
public sealed record ConversationImportRequest(string Text, string SourceType);
