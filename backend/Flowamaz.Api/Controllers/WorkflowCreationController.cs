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
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using YamlDotNet.Serialization;

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
    private readonly IWorkflowValidator _validator;
    private readonly WorkflowService _workflows;
    private readonly ICurrentUserService _currentUser;
    private readonly INlClarificationService _clarification;
    private readonly IValidator<NlWorkflowRequest> _nlValidator;
    private readonly IValidator<ConfirmedVisualGraph> _graphValidator;
    private readonly IValidator<VisualPreviewRequest> _previewValidator;
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
        IWorkflowValidator validator,
        WorkflowService workflows,
        ICurrentUserService currentUser,
        INlClarificationService clarification,
        IValidator<NlWorkflowRequest> nlValidator,
        IValidator<ConfirmedVisualGraph> graphValidator,
        IValidator<VisualPreviewRequest> previewValidator,
        ILogger<WorkflowCreationController> log)
    {
        _clarification = clarification;
        _nlValidator = nlValidator;
        _graphValidator = graphValidator;
        _previewValidator = previewValidator;
        _generator = generator;
        _copilot = copilot;
        _visualInput = visualInput;
        _conversationImport = conversationImport;
        _sopParsing = sopParsing;
        _validator = validator;
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

                    await _nlValidator.ValidateAndThrowAsync(request.NlRequest, cancellationToken);
                    var gen = await _generator.GenerateAsync(request.NlRequest, workspaceId, cancellationToken);
                    yamlContent = gen.YamlContent;
                    validationResult = gen.ValidationResult;
                    tokensUsed = gen.TokensUsed;
                    cached = gen.Cached;
                    break;
                }

                case "visual":
                {
                    // Review path: the user confirmed the graph from POST /visual/preview. No AI call —
                    // YAML is built from exactly what they kept/renamed, then validated below.
                    if (request.VisualGraph is not null)
                    {
                        await _graphValidator.ValidateAndThrowAsync(request.VisualGraph, cancellationToken);
                        yamlContent = _visualInput.BuildYamlFromConfirmedGraph(request.VisualGraph, slug, request.Name);
                        break;
                    }

                    // One-shot path (API clients that skip review): low-confidence nodes are kept and
                    // marked config.review_needed: true rather than dropped.
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
                    yamlContent = BuildCanvasSkeleton(slug, request.Name);
                    break;
            }
        }
        catch (Exception ex) when (ex is not WorkflowSlugExistsException and not AppException
                                       and not OperationCanceledException and not ValidationException)
        {
            // Provider failures (bad key, throttling, outage) arrive as AiProviderException (an AppException)
            // with their own actionable message. Anything else is unexpected: log it, never echo ex.Message.
            _log.LogError(ex, "WorkflowCreationController.CreateWorkflow AI service error method={Method}", method);
            return StatusCode(502, new
            {
                error = UnexpectedAiFailure("generate the workflow"),
                code = "AI_PROCESSING_FAILED",
            });
        }

        // Never persist a generated definition that does not validate. Saving invalid YAML produced
        // drafts that could not be opened, published or run, and hid AI misconfiguration.
        if (method != "canvas")
        {
            validationResult ??= await _validator.ValidateAsync(yamlContent, workspaceId, cancellationToken);
            if (!validationResult.IsValid)
            {
                var first = validationResult.Errors.FirstOrDefault()?.Message ?? "The generated definition could not be parsed.";
                _log.LogWarning("WorkflowCreationController.CreateWorkflow rejected invalid generated YAML method={Method} errors={Count}",
                    method, validationResult.Errors.Count);
                return UnprocessableEntity(new
                {
                    error = $"We couldn't build a valid workflow from that input ({validationResult.Errors.Count} problem(s); first: {first}). " +
                            "Add more detail about the steps and who does them, then try again — or start from the canvas.",
                    code = "WORKFLOW_GENERATION_INVALID",
                    validation = validationResult,
                });
            }
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
    /// NL creation step 1 (optional): returns up to 3 clarifying questions about missing essentials
    /// (trigger, approver, rejection path, systems). Empty list = generate straight away. Answers go
    /// back in <c>nlRequest.clarifications</c> on POST /create. Heuristics first; AI only for very thin input.
    /// </summary>
    [HttpPost("clarify")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<ClarificationResult>> Clarify(
        Guid workspaceId, [FromBody] NlWorkflowRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.Clarify entry workspaceId={WorkspaceId}", workspaceId);
        await _nlValidator.ValidateAndThrowAsync(request, cancellationToken);

        var result = await _clarification.GetQuestionsAsync(request, workspaceId, cancellationToken);

        _log.LogInformation("WorkflowCreationController.Clarify exit questions={Count} usedAi={UsedAi}", result.Questions.Count, result.UsedAi);
        return Ok(result);
    }

    /// <summary>
    /// Visual creation step 1: parse the image and return the full detected graph for review. Nodes under
    /// the confidence threshold are flagged (<c>reviewNeeded</c>) with the AI's reason. Nothing is saved;
    /// POST /create with method=visual and <c>visualGraph</c> creates the workflow from the confirmed graph.
    /// </summary>
    [HttpPost("visual/preview")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<ActionResult<VisualPreviewResult>> PreviewVisual(
        Guid workspaceId, [FromBody] VisualPreviewRequest request, CancellationToken cancellationToken)
    {
        _log.LogInformation("WorkflowCreationController.PreviewVisual entry workspaceId={WorkspaceId}", workspaceId);
        await _previewValidator.ValidateAndThrowAsync(request, cancellationToken);

        var imageBytes = Convert.FromBase64String(request.ImageBase64);
        var mimeType = (request.ImageMimeType ?? "image/jpeg").ToLowerInvariant();

        VisualPreviewResult preview;
        try
        {
            preview = await _visualInput.PreviewImageAsync(imageBytes, mimeType, workspaceId, cancellationToken);
        }
        catch (Exception ex) when (ex is not AppException and not OperationCanceledException)
        {
            _log.LogError(ex, "WorkflowCreationController.PreviewVisual AI service error workspaceId={WorkspaceId}", workspaceId);
            return StatusCode(502, new
            {
                error = UnexpectedAiFailure("analyse the diagram"),
                code = "AI_PROCESSING_FAILED",
            });
        }

        if (preview.Nodes.Count == 0)
        {
            _log.LogWarning("WorkflowCreationController.PreviewVisual nothing detected workspaceId={WorkspaceId}", workspaceId);
            return UnprocessableEntity(new
            {
                error = "We couldn't find any workflow steps in that image. Use a clearer, well-lit photo where boxes and arrows " +
                        "are visible, crop out the background, then upload again — or start from the canvas.",
                code = "VISUAL_NOTHING_DETECTED",
            });
        }

        _log.LogInformation("WorkflowCreationController.PreviewVisual exit nodes={Nodes} reviewNeeded={Review}",
            preview.Nodes.Count, preview.Nodes.Count(n => n.ReviewNeeded));
        return Ok(preview);
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
        CopilotResult result;
        try
        {
            result = await _copilot.ProcessCommandAsync(request.Command, request.YamlContent, workspaceId, userId, cancellationToken);
        }
        catch (Exception ex) when (ex is not AppException and not OperationCanceledException)
        {
            // AppExceptions (e.g. AiProviderException for a rejected AI key — B9) propagate to the global
            // middleware with their own actionable message and status. Only the unexpected lands here.
            _log.LogError(ex, "WorkflowCreationController.Copilot service error workspaceId={WorkspaceId}", workspaceId);
            return StatusCode(502, new
            {
                error = UnexpectedAiFailure("process the Co-pilot command"),
                code = "AI_PROCESSING_FAILED",
            });
        }

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

    /// <summary>
    /// Actionable text for an unexpected (non-provider) AI pipeline failure. The raw exception is logged,
    /// never returned — it can carry internal detail and tells the user nothing they can act on.
    /// </summary>
    internal static string UnexpectedAiFailure(string action) =>
        $"AI processing failed: Flowamaz could not {action} because of an unexpected server error (details are in the server log). " +
        "Nothing was saved. Try once more; if it fails again, start from the canvas instead and contact support@flowamaz.io " +
        "with the time of the request.";

    private static readonly ISerializer YamlWriter = new SerializerBuilder().Build();

    /// <summary>Empty canvas definition, serialised via YamlDotNet (CLAUDE.md rule 5) so the
    /// user-supplied name can never break or inject into the YAML document.</summary>
    internal static string BuildCanvasSkeleton(string slug, string name)
    {
        var doc = new Dictionary<string, object>
        {
            ["apiVersion"] = "flowamaz/v1",
            ["kind"] = "Workflow",
            ["metadata"] = new Dictionary<string, object> { ["id"] = slug, ["name"] = name },
            ["spec"] = new Dictionary<string, object>
            {
                ["trigger"] = new Dictionary<string, object> { ["type"] = "manual" },
                ["nodes"] = Array.Empty<object>(),
                ["edges"] = Array.Empty<object>(),
            },
        };
        return YamlWriter.Serialize(doc);
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
    string? DocumentMimeType = null,
    ConfirmedVisualGraph? VisualGraph = null
);

/// <summary>Body of POST /workflows/visual/preview.</summary>
public sealed record VisualPreviewRequest(string ImageBase64, string? ImageMimeType = null);

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
