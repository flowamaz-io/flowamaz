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

    /// <summary>True when the account was just-in-time provisioned via SSO (no password login).</summary>
    public bool IsSsoProvisioned { get; set; }

    /// <summary>
    /// The IdP identity this account is linked to (set on first SSO sign-in). Later SSO sign-ins must present the same
    /// subject, so changing an email at the IdP can't take over another account.
    /// </summary>
    public string? SsoSubject { get; set; }

    /// <summary>The IdP's id for this person when provisioned over SCIM (SCIM <c>externalId</c>).</summary>
    public string? ScimExternalId { get; set; }

    /// <summary>The SCIM <c>userName</c> the IdP uses (often the email; Entra sends the UPN).</summary>
    public string? ScimUserName { get; set; }

    /// <summary>
    /// Set when the IdP deleted the user over SCIM: the account is deactivated and no longer listed over SCIM.
    /// Creating the same person again over SCIM reuses the account.
    /// </summary>
    public DateTime? ScimDeletedAt { get; set; }
}
