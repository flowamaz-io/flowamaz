namespace Flowamaz.Core.Entities.Auth;

/// <summary>
/// A bearer token an identity provider uses to provision users and groups over SCIM 2.0 for one organisation
/// (Enterprise). Only the SHA-256 hash is stored; the plain token is shown once, when it is created.
/// </summary>
public class ScimToken : BaseEntity
{
    public Guid OrgId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex, lower-case) of the plain token. Unique.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>First characters of the plain token, for recognising it in the list. Not secret.</summary>
    public string TokenPrefix { get; set; } = string.Empty;

    public Guid CreatedBy { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>A group pushed by the IdP over SCIM. Its display name is what SSO group → role mappings match.</summary>
public class ScimGroup : BaseEntity
{
    public Guid OrgId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
}

/// <summary>A user's membership of a SCIM group.</summary>
public class ScimGroupMember : BaseEntity
{
    public Guid GroupId { get; set; }
    public Guid OrgUserId { get; set; }
}

/// <summary>
/// A person whose workspace roles must be re-applied from their SCIM groups, waiting for the background job
/// (ScimRoleResyncJob). A group change that touches many people saves the membership and queues them here in the
/// same transaction, so the IdP's request returns quickly and no change is lost if the server restarts.
/// One row per person and organisation.
/// </summary>
public class ScimRoleResync : BaseEntity
{
    public Guid OrgId { get; set; }
    public Guid OrgUserId { get; set; }

    /// <summary>Failed runs so far. Rows that keep failing go to the back of the queue and stop after a few tries.</summary>
    public int Attempts { get; set; }
}
