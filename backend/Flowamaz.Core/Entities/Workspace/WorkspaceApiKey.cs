namespace Flowamaz.Core.Entities.Workspaces;

/// <summary>
/// A workspace API key. Only the SHA256 <see cref="KeyHash"/> is persisted — the plain key is
/// returned exactly once at creation and never again, never logged (FUNCTIONAL.md §12.5).
/// <see cref="KeyPrefix"/> is a short, non-sensitive display fragment. Revocation flips
/// <see cref="IsActive"/> rather than deleting, preserving the audit trail.
/// </summary>
public class WorkspaceApiKey : BaseEntity
{
    public Guid WorkspaceId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA256 (hex, lower-case) of the plain key. Unique. Never reversible to the plain value.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>First 16 chars of the plain key (e.g. <c>fmz_live_finops_</c>) — display only, not secret.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];
    public Guid CreatedBy { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}
