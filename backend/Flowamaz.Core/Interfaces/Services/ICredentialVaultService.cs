namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Alias returned by <see cref="ICredentialVaultService.ListAsync"/>. Never contains the
/// encrypted value or any secret material.
/// </summary>
public record CredentialAlias(
    Guid Id,
    string Name,
    string AuthType,
    DateTime? ExpiresAt,
    bool IsActive);

/// <summary>
/// Secure credential vault: store/retrieve/revoke AES-256-GCM encrypted credentials.
/// The plain value is NEVER logged and is only returned to the caller of
/// <see cref="RetrieveAsync"/>.
/// </summary>
public interface ICredentialVaultService
{
    /// <summary>
    /// Encrypts <paramref name="plainValue"/> and stores it. Returns the new credential ID.
    /// </summary>
    Task<Guid> StoreAsync(
        Guid workspaceId,
        string connectorId,
        string name,
        string authType,
        string plainValue,
        CancellationToken ct = default);

    /// <summary>
    /// Decrypts and returns the plain credential value. Validates that the credential
    /// belongs to <paramref name="workspaceId"/> before decrypting.
    /// </summary>
    Task<string> RetrieveAsync(Guid workspaceId, Guid credentialId, CancellationToken ct = default);

    /// <summary>Marks the credential inactive; does not delete the row.</summary>
    Task RevokeAsync(Guid workspaceId, Guid credentialId, CancellationToken ct = default);

    /// <summary>
    /// Lists all credentials for the workspace. NEVER returns encrypted material.
    /// </summary>
    Task<IReadOnlyList<CredentialAlias>> ListAsync(Guid workspaceId, CancellationToken ct = default);
}
