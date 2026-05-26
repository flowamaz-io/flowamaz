namespace Flowamaz.Core.Entities.Connector;

/// <summary>
/// Encrypted credential stored per workspace. The plain value is NEVER stored or returned;
/// only the AES-256-GCM ciphertext + envelope is persisted.
/// </summary>
public class WorkspaceCredential : WorkspaceEntity
{
    public Guid ConnectorDefinitionId { get; set; }

    /// <summary>Friendly alias shown in UI; never contains the secret value.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Auth type string, e.g. "ApiKey", "OAuth2AuthCode".</summary>
    public string AuthType { get; set; } = string.Empty;

    // AES-256-GCM envelope -------------------------------------------------------

    /// <summary>AES-256-GCM ciphertext of the credential value.</summary>
    public byte[] EncryptedValue { get; set; } = [];

    /// <summary>DEK (data encryption key) wrapped with workspace master key.</summary>
    public byte[] EncryptedKey { get; set; } = [];

    /// <summary>AES-GCM nonce (12 bytes).</summary>
    public byte[] IV { get; set; } = [];

    /// <summary>AES-GCM authentication tag (16 bytes).</summary>
    public byte[] Tag { get; set; } = [];

    // Lifecycle ------------------------------------------------------------------
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public virtual ConnectorDefinition? ConnectorDefinition { get; set; }
}
