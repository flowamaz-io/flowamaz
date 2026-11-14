using System.ComponentModel.DataAnnotations.Schema;

namespace Flowamaz.Core.Entities.Auth;

/// <summary>
/// A signed-in `fmz` CLI. Created when someone approves a device-flow login in the browser. The CLI holds a
/// long-lived refresh token for it (only the SHA256 <see cref="TokenHash"/> is stored here) and trades it for
/// 15-minute access tokens with POST /api/v1/cli/auth/refresh.
/// <para>
/// Kept apart from <see cref="RefreshToken"/> on purpose: a browser cookie can never refresh a CLI session and a
/// CLI token can never refresh a browser one. The token is not rotated on use, so two `fmz` commands running at
/// once can't sign each other out; instead the session expires after <c>Jwt:CliSessionIdleDays</c> without use,
/// and the person can revoke it any time from Settings → CLI sessions or with `fmz auth logout`.
/// </para>
/// </summary>
public class CliSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgUserId { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>SHA256 (hex, lower-case) of the plain refresh token. Unique.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>What the CLI called itself at login (usually the machine's host name). Shown in Settings.</summary>
    public string Name { get; set; } = string.Empty;

    public string CreatedByIp { get; set; } = string.Empty;
    public string? LastUsedIp { get; set; }
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Slides forward on every refresh; the session ends if it isn't used before then.</summary>
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// The jti and expiry of the newest access token handed to this CLI, so revoking the session also cuts off
    /// that token at once instead of letting it run out its 15 minutes.
    /// </summary>
    public string? CurrentAccessTokenId { get; set; }
    public DateTime? CurrentAccessTokenExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True once revoked or past expiry — such a session must never be refreshed.</summary>
    [NotMapped]
    public bool IsActive => !RevokedAt.HasValue && ExpiresAt > DateTime.UtcNow;
}
