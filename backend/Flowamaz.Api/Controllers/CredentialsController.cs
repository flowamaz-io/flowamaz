using Flowamaz.Api.Authorization;
using Flowamaz.Application.Connectors;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workspace credential vault (FUNCTIONAL.md §12.4). Only aliases are ever returned — values are
/// write-only and never logged. Listing (for the node-config credential picker) needs Designer;
/// creating and revoking needs WorkspaceAdmin (<c>credentials.manage</c>, §4.5). OAuth credentials
/// are created through the connector OAuth flow instead.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/credentials")]
public sealed class CredentialsController : ControllerBase
{
    private const int MaxValueLength = 16 * 1024;
    private static readonly string[] ManualAuthTypes = ["ApiKey", "BearerToken", "BasicAuth", "ConnectionString", "HmacSecret"];

    private readonly ICredentialVaultService _vault;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService? _audit;
    private readonly ILogger<CredentialsController> _log;

    public CredentialsController(
        ICredentialVaultService vault, ICurrentUserService currentUser, ILogger<CredentialsController> log, IAuditService? audit = null)
    {
        _vault = vault;
        _currentUser = currentUser;
        _log = log;
        _audit = audit;
    }

    /// <summary>List credential aliases, optionally for one connector. Never returns values.</summary>
    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<IReadOnlyList<CredentialAlias>>> List(
        Guid workspaceId, [FromQuery] string? connectorId, CancellationToken ct)
    {
        _log.LogInformation("CredentialsController.List entry workspace={WorkspaceId} connector={ConnectorId}", workspaceId, connectorId);
        var aliases = await _vault.ListAsync(workspaceId, ct);
        var result = string.IsNullOrWhiteSpace(connectorId)
            ? aliases
            : aliases.Where(a => string.Equals(a.ConnectorId, connectorId, StringComparison.Ordinal)).ToList();
        _log.LogInformation("CredentialsController.List exit count={Count}", result.Count);
        return Ok(result);
    }

    /// <summary>Store a new API key / token / connection string / HMAC secret. Returns the alias only.</summary>
    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<CredentialAlias>> Create(Guid workspaceId, [FromBody] CreateCredentialRequest request, CancellationToken ct)
    {
        _log.LogInformation("CredentialsController.Create entry workspace={WorkspaceId} connector={ConnectorId} authType={AuthType}",
            workspaceId, request.ConnectorId, request.AuthType);

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
            return BadRequest(new { error = "Give the credential a name of 1–100 characters, e.g. 'Production Postgres'." });
        if (string.IsNullOrEmpty(request.Value) || request.Value.Length > MaxValueLength)
            return BadRequest(new { error = "Enter the secret value (up to 16 KB)." });
        if (!ManualAuthTypes.Contains(request.AuthType))
            return BadRequest(new { error = $"authType must be one of: {string.Join(", ", ManualAuthTypes)}. OAuth accounts are connected with 'Connect with OAuth'." });

        var spec = OfficialConnectorCatalog.FindConnector(request.ConnectorId ?? string.Empty);
        if (spec is not null && spec.CredentialTypes.Count > 0 && !spec.CredentialTypes.Contains(request.AuthType))
            return BadRequest(new { error = $"The {spec.ConnectorId} connector accepts {string.Join(" or ", spec.CredentialTypes)} credentials, not {request.AuthType}." });
        if (request.AuthType == "BasicAuth" && !request.Value.Contains(':'))
            return BadRequest(new { error = "Basic auth values are 'username:password'." });

        var existing = await _vault.ListAsync(workspaceId, ct);
        if (existing.Any(a => a.IsActive && a.ConnectorId == request.ConnectorId && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = $"A {request.ConnectorId} credential named '{name}' already exists. Choose another name or revoke the old one." });

        Guid id;
        try
        {
            id = await _vault.StoreAsync(workspaceId, request.ConnectorId!, name, request.AuthType, request.Value, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            WorkspaceId = workspaceId,
            ActorUserId = _currentUser.UserId,
            ActorType = "user",
            EventType = "credential.created",
            ResourceType = "credential",
            ResourceId = id,
            ResourceLabel = name,
            Action = "created",
            Metadata = new { connectorId = request.ConnectorId, authType = request.AuthType },
        }, ct);

        _log.LogInformation("CredentialsController.Create exit credential={CredentialId}", id);
        return Ok(new CredentialAlias(id, name, request.AuthType, null, true, request.ConnectorId));
    }

    /// <summary>Revoke a credential. Workflows using it fail with an actionable error until re-pointed.</summary>
    [HttpDelete("{credentialId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> Revoke(Guid workspaceId, Guid credentialId, CancellationToken ct)
    {
        _log.LogInformation("CredentialsController.Revoke entry workspace={WorkspaceId} credential={CredentialId}", workspaceId, credentialId);
        try
        {
            await _vault.RevokeAsync(workspaceId, credentialId, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound(new { error = "Credential not found in this workspace." });
        }

        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            WorkspaceId = workspaceId,
            ActorUserId = _currentUser.UserId,
            ActorType = "user",
            EventType = "credential.revoked",
            ResourceType = "credential",
            ResourceId = credentialId,
            Action = "revoked",
        }, ct);

        _log.LogInformation("CredentialsController.Revoke exit credential={CredentialId}", credentialId);
        return NoContent();
    }
}

public sealed record CreateCredentialRequest(string? ConnectorId, string? Name, string AuthType, string? Value);
