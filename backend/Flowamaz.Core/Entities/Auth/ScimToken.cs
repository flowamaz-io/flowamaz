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
