namespace Flowamaz.Core.Entities.Webhooks;

/// <summary>
/// A per-workflow inbound webhook endpoint. Each row exposes a public receive URL
/// (<c>/webhooks/{Id}</c>) whose payloads are authenticated with an HMAC-SHA256 signature
/// over the raw request body. The HMAC secret is encrypted at rest with the credential
/// master key (see <c>ISecretProtector</c>) and is only ever returned in plaintext once,
/// at creation or rotation. Workspace-scoped: every management query filters by WorkspaceId.
/// </summary>
public class WebhookEndpoint : WorkspaceEntity
{
    /// <summary>The published workflow this endpoint triggers.</summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>HMAC secret, encrypted at rest (base64 of nonce||ciphertext||tag). Never logged.</summary>
    public string EncryptedSecret { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string? Description { get; set; }

    /// <summary>Optional IP allowlist. Empty = allow any source IP.</summary>
    public List<string> AllowedIps { get; set; } = [];
}
