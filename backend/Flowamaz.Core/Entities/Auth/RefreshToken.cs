using System.ComponentModel.DataAnnotations.Schema;

namespace Flowamaz.Core.Entities.Auth;

/// <summary>
/// A rotating refresh token. Only the SHA256 <see cref="TokenHash"/> is stored — the plain token
/// lives in the httpOnly cookie and in request memory only, never persisted or logged
/// (FUNCTIONAL.md §12.1). On every use the current token is revoked and a new one issued (rotation).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgUserId { get; set; }

    /// <summary>SHA256 (hex, lower-case) of the plain token. Unique.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByIp { get; set; } = string.Empty;

    /// <summary>True once revoked or past expiry — such a token must never be accepted.</summary>
    [NotMapped]
    public bool IsRevoked => RevokedAt.HasValue || ExpiresAt < DateTime.UtcNow;
}
