using System.ComponentModel.DataAnnotations.Schema;

namespace Flowamaz.Core.Entities.Auth;

/// <summary>
/// One-time password reset token. Only the SHA256 <see cref="TokenHash"/> is stored —
/// the plain token travels in the email link only. Token expires after 1 hour and can
/// only be used once (<see cref="UsedAt"/> set on redemption).
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public bool IsValid => UsedAt is null && ExpiresAt > DateTime.UtcNow;
}
