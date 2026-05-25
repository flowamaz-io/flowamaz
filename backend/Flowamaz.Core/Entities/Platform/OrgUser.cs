namespace Flowamaz.Core.Entities.Platform;

/// <summary>
/// A login identity within an organisation. Email is unique <em>per org</em> (composite index),
/// not globally — the same person can own accounts in multiple orgs. Soft-deletable.
/// <see cref="PasswordHash"/> is a BCrypt hash (cost 12) and is never returned or logged.
/// </summary>
public class OrgUser : BaseEntity
{
    public Guid OrgId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsOrgOwner { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public int FailedLoginCount { get; set; }
}
