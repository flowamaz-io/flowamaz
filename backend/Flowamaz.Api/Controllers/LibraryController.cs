using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowamaz.Api.Authorization;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Core.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// The Library for one workspace (FUNCTIONAL.md §23.5), as the <c>fmz library</c> commands and the Library
/// screens use it: browse, install, see what needs an upgrade, upgrade a connector's major, publish.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/library")]
public sealed class LibraryController : ControllerBase
{
    private readonly ILibraryService _library;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<LibraryPublishRequest> _publishValidator;
    private readonly ILogger<LibraryController> _log;

    public LibraryController(ILibraryService library, ICurrentUserService currentUser, IValidator<LibraryPublishRequest> publishValidator, ILogger<LibraryController> log)
    {
        _library = library;
        _currentUser = currentUser;
        _publishValidator = publishValidator;
        _log = log;
    }

    [HttpGet("connectors")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<LibraryConnectorItem>>> Connectors(Guid workspaceId, [FromQuery] string? q, [FromQuery] string? category, CancellationToken ct) =>
        Ok(await _library.ListConnectorsAsync(workspaceId, q, category, ct));

    /// <summary>Installed connectors with a newer major to upgrade to, or a deprecation notice.</summary>
    [HttpGet("connectors/outdated")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<LibraryOutdatedItem>>> Outdated(Guid workspaceId, CancellationToken ct) =>
        Ok(await _library.GetOutdatedConnectorsAsync(workspaceId, ct));

    [HttpGet("connectors/{*connectorId}")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<LibraryConnectorDetail>> Connector(Guid workspaceId, string connectorId, CancellationToken ct) =>
        Ok(await _library.GetConnectorAsync(workspaceId, connectorId, ct));

    [HttpPost("connectors/install")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<LibraryConnectorItem>> Install(Guid workspaceId, [FromBody] LibraryItemRequest request, CancellationToken ct) =>
        Ok(await _library.InstallConnectorAsync(workspaceId, request.Id, _currentUser.UserId!.Value, ct));

    /// <summary>Moves an installed connector to the latest major. Minor/patch releases need nothing: they are automatic.</summary>
    [HttpPost("connectors/update")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<LibraryUpdateResult>> Update(Guid workspaceId, [FromBody] LibraryItemRequest request, CancellationToken ct) =>
        Ok(await _library.UpgradeConnectorAsync(workspaceId, request.Id, _currentUser.UserId!.Value, ct));

    [HttpGet("templates")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<LibraryTemplateItem>>> Templates(Guid workspaceId, [FromQuery] string? q, [FromQuery] string? category, CancellationToken ct) =>
        Ok(await _library.ListTemplatesAsync(workspaceId, q, category, ct));

    /// <summary>Templates are copied on install, so there is nothing to update; says so instead of an empty list.</summary>
    [HttpGet("templates/outdated")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public ActionResult TemplatesOutdated(Guid workspaceId) => throw LibraryException.TemplatesDontUpdate();

    [HttpGet("templates/{slugOrId}")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<LibraryTemplateItem>> Template(Guid workspaceId, string slugOrId, CancellationToken ct) =>
        Ok(await _library.GetTemplateAsync(workspaceId, slugOrId, ct));

    [HttpPost("templates/install")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<object>> InstallTemplate(Guid workspaceId, [FromBody] LibraryItemRequest request, CancellationToken ct)
    {
        var workflowId = await _library.InstallTemplateAsync(workspaceId, request.Id, request.Name, _currentUser.UserId!.Value, ct);
        return Ok(new { workflowId });
    }

    /// <summary>`fmz connector publish`: opens the review pull request against the connectors repository.</summary>
    [HttpPost("connectors/publish")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<LibraryPublishResult>> PublishConnector(Guid workspaceId, [FromBody] LibraryPublishRequest request, CancellationToken ct)
    {
        await _publishValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _library.PublishConnectorAsync(workspaceId, _currentUser.OrgId!.Value, request.Manifest, ct));
    }

    /// <summary>`fmz template publish`: a community template, pending review.</summary>
    [HttpPost("templates/publish")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<LibraryPublishResult>> PublishTemplate(Guid workspaceId, [FromBody] LibraryPublishRequest request, CancellationToken ct)
    {
        await _publishValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _library.PublishTemplateAsync(workspaceId, _currentUser.OrgId!.Value, _currentUser.UserId!.Value, request.Manifest, ct));
    }
}

/// <summary>A connector id (or publisher/id) or a template slug/id; Name only for template installs.</summary>
public sealed record LibraryItemRequest(string Id, string? Name = null);

public sealed record LibraryPublishRequest(string Manifest);

public sealed class LibraryPublishRequestValidator : AbstractValidator<LibraryPublishRequest>
{
    public LibraryPublishRequestValidator()
    {
        RuleFor(x => x.Manifest).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("The manifest is empty. Run `fmz connector init <name>` (or `fmz template init`) to create one.")
            .MaximumLength(256 * 1024).WithMessage("The manifest is larger than 256 KB. Move examples and docs into README.md.");
    }
}

/// <summary>
/// GitHub push webhook for the Library repositories (FUNCTIONAL.md §23.7): a push to the default branch starts a
/// sync straight away instead of waiting for the 15-minute poll. Signed with <c>Library:WebhookSecret</c>
/// (X-Hub-Signature-256); under <c>/webhooks</c>, so no JWT.
/// </summary>
[ApiController]
[Route("webhooks/library/github")]
[AllowAnonymous]
public sealed class LibraryWebhookController : ControllerBase
{
    private readonly ILibrarySyncService _sync;
    private readonly IConfiguration _config;
    private readonly ILogger<LibraryWebhookController> _log;

    public LibraryWebhookController(ILibrarySyncService sync, IConfiguration config, ILogger<LibraryWebhookController> log)
    {
        _sync = sync;
        _config = config;
        _log = log;
    }

    [HttpPost]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Push(CancellationToken ct)
    {
        _log.LogInformation("LibraryWebhookController.Push entry event={Event}", Request.Headers["X-GitHub-Event"].ToString());
        var secret = _config["Library:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return StatusCode(503, new { error = "The Library webhook isn't configured on this deployment (Library:WebhookSecret). The 15-minute sync still runs." });

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        if (!SignatureMatches(secret, body, Request.Headers["X-Hub-Signature-256"].ToString()))
        {
            _log.LogWarning("LibraryWebhookController.Push rejected: signature mismatch");
            return Unauthorized(new { error = "The signature doesn't match. Set the same secret in the GitHub webhook and Library:WebhookSecret." });
        }

        var gitHubEvent = Request.Headers["X-GitHub-Event"].ToString();
        if (gitHubEvent == "ping") return Ok(new { status = "pong" });
        if (gitHubEvent != "push") return Accepted(new { status = "ignored", reason = $"Only push events start a sync, not '{gitHubEvent}'." });

        var branch = _config["Library:Branch"] is { Length: > 0 } b ? b : "main";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var pushedRef = doc.RootElement.TryGetProperty("ref", out var r) ? r.GetString() : null;
            if (pushedRef != $"refs/heads/{branch}")
                return Accepted(new { status = "ignored", reason = $"Push to {pushedRef}, not {branch}." });
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "The webhook body isn't JSON. Set the GitHub webhook's content type to application/json." });
        }

        await _sync.RequestSyncAsync(ct);
        _log.LogInformation("LibraryWebhookController.Push exit: sync requested");
        return Accepted(new { status = "sync_requested" });
    }

    internal static bool SignatureMatches(string secret, string body, string header)
    {
        if (!header.StartsWith("sha256=", StringComparison.Ordinal)) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        byte[] given;
        try
        {
            given = Convert.FromHexString(header["sha256=".Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }
}

/// <summary>Library sync for Flowamaz staff: where each repository stands, and a sync on demand.</summary>
[ApiController]
[Route("api/v1/platform/library")]
[RequirePlatformStaff]
public sealed class PlatformLibraryController : ControllerBase
{
    private readonly ILibrarySyncService _sync;

    public PlatformLibraryController(ILibrarySyncService sync) => _sync = sync;

    [HttpGet("sync")]
    public async Task<ActionResult<IReadOnlyList<LibrarySyncStatus>>> Status(CancellationToken ct) => Ok(await _sync.GetStatusAsync(ct));

    [HttpPost("sync")]
    public async Task<ActionResult<IReadOnlyList<LibrarySyncReport>>> SyncNow(CancellationToken ct) => Ok(await _sync.SyncAsync(ct));
}
