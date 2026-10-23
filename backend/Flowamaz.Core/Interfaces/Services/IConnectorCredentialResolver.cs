using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Resolves and decrypts the workspace credential a connector call should use.
/// Workspace-isolated: only credentials of <c>workspaceId</c> for <c>connectorId</c> are considered.
/// </summary>
public interface IConnectorCredentialResolver
{
    /// <summary>
    /// <paramref name="credentialRef"/> is the alias name or id from the node config. When null, the
    /// workspace's single active credential for the connector is used; none → null; several → throws.
    /// Throws <see cref="Flowamaz.Core.Exceptions.ConnectorCredentialException"/> when a named credential
    /// is missing, revoked, ambiguous or belongs to another connector. OAuth tokens near expiry are
    /// refreshed when the provider supports it.
    /// </summary>
    Task<ConnectorCredential?> ResolveAsync(
        Guid workspaceId, string connectorId, string? credentialRef, CancellationToken ct = default);
}
