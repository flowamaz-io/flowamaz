using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Finds the workspace credential for a connector call and decrypts it through the vault.
/// Every query filters by workspace_id (CLAUDE.md rule 1) and by the connector, so a node can never
/// use another workspace's credential or another connector's secret. Secrets are never logged.
/// </summary>
public sealed class ConnectorCredentialResolver : IConnectorCredentialResolver
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly FlowAmazDbContext _db;
    private readonly ICredentialVaultService _vault;
    private readonly IOAuthService _oauth;
    private readonly ILogger<ConnectorCredentialResolver> _logger;

    public ConnectorCredentialResolver(
        FlowAmazDbContext db, ICredentialVaultService vault, IOAuthService oauth, ILogger<ConnectorCredentialResolver> logger)
    {
        _db = db;
        _vault = vault;
        _oauth = oauth;
        _logger = logger;
    }

    public async Task<ConnectorCredential?> ResolveAsync(
        Guid workspaceId, string connectorId, string? credentialRef, CancellationToken ct = default)
    {
        _logger.LogDebug("ConnectorCredentialResolver.ResolveAsync enter workspace={WorkspaceId} connector={ConnectorId} hasRef={HasRef}",
            workspaceId, connectorId, !string.IsNullOrWhiteSpace(credentialRef));

        try
        {
            var candidates = await _db.WorkspaceCredentials.AsNoTracking()
                .Where(c => c.WorkspaceId == workspaceId && !c.IsDeleted && c.IsActive
                            && c.ConnectorDefinition != null && c.ConnectorDefinition.ConnectorId == connectorId)
                .Select(c => new { c.Id, c.Name, c.AuthType })
                .ToListAsync(ct);

            var match = Select(candidates.Select(c => (c.Id, c.Name, c.AuthType)).ToList(), connectorId, credentialRef);
            if (match is null)
            {
                _logger.LogDebug("ConnectorCredentialResolver.ResolveAsync exit — no credential for {ConnectorId}", connectorId);
                return null;
            }

            var (id, name, authType) = match.Value;
            var secret = await _vault.RetrieveAsync(workspaceId, id, ct);
            secret = await RefreshIfExpiredAsync(workspaceId, connectorId, id, name, secret, ct);

            _logger.LogDebug("ConnectorCredentialResolver.ResolveAsync exit credential={CredentialId}", id);
            return new ConnectorCredential(id, name, authType, secret);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Revoked between lookup and decrypt, or ownership mismatch — never fall back silently.
            throw new ConnectorCredentialException(ex.Message);
        }
    }

    internal static (Guid Id, string Name, string AuthType)? Select(
        IReadOnlyList<(Guid Id, string Name, string AuthType)> candidates, string connectorId, string? credentialRef)
    {
        if (!string.IsNullOrWhiteSpace(credentialRef))
        {
            var reference = credentialRef.Trim();
            var byId = Guid.TryParse(reference, out var refId) ? candidates.Where(c => c.Id == refId).ToList() : [];
            var hits = byId.Count > 0
                ? byId
                : candidates.Where(c => string.Equals(c.Name, reference, StringComparison.OrdinalIgnoreCase)).ToList();

            return hits.Count switch
            {
                1 => hits[0],
                0 => throw new ConnectorCredentialException(
                    $"Credential '{reference}' was not found for the {connectorId} connector in this workspace (or it was revoked). " +
                    "Open Settings → Credentials to connect it, or pick an existing credential in the node config."),
                _ => throw new ConnectorCredentialException(
                    $"More than one {connectorId} credential is named '{reference}'. Rename one in Settings → Credentials, " +
                    "or reference the credential by id in the node config."),
            };
        }

        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new ConnectorCredentialException(
                $"This workspace has {candidates.Count} {connectorId} credentials and the node does not say which to use. " +
                "Pick one in the node's Credential field."),
        };
    }

    private async Task<string> RefreshIfExpiredAsync(
        Guid workspaceId, string connectorId, Guid credentialId, string name, string secret, CancellationToken ct)
    {
        var bundle = OAuthTokenBundle.TryParse(secret);
        if (bundle is null || !bundle.IsExpired(DateTime.UtcNow, RefreshSkew)) return secret;

        if (string.IsNullOrEmpty(bundle.RefreshToken))
            throw new ConnectorCredentialException(
                $"The {connectorId} sign-in for credential '{name}' has expired and cannot be refreshed. Reconnect it in Settings → Credentials.");

        OAuthTokenBundle? refreshed;
        try
        {
            refreshed = await _oauth.RefreshAsync(connectorId, bundle.RefreshToken, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ConnectorCredentialResolver: token refresh failed credential={CredentialId}", credentialId);
            throw new ConnectorCredentialException(
                $"The {connectorId} sign-in for credential '{name}' expired and refreshing it failed. Reconnect it in Settings → Credentials.");
        }

        if (refreshed is null) return secret;

        var json = refreshed.ToJson();
        await _vault.UpdateValueAsync(workspaceId, credentialId, json, ct);
        _logger.LogInformation("ConnectorCredentialResolver: refreshed OAuth token credential={CredentialId}", credentialId);
        return json;
    }
}
