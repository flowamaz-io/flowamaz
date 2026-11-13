namespace Flowamaz.Core.Entities.Library;

/// <summary>
/// A Library publisher's public profile (FUNCTIONAL.md §23.9, contributor profiles). <see cref="PublisherId"/> is
/// the id used in Library folders and pins (<c>acme</c> in <c>acme/invoice-sync@1</c>): an organisation publishes
/// under its slug. Publishers without a row still have a profile page, built from their items.
/// </summary>
public class PublisherProfile : AuditableEntity
{
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>The organisation that owns this publisher id and may edit the profile; null for flowamaz-io.</summary>
    public Guid? OrgId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string? WebsiteUrl { get; set; }
    public string? GithubLogin { get; set; }

    /// <summary>Set by platform staff only (the official publisher is always verified).</summary>
    public bool IsVerified { get; set; }
}
