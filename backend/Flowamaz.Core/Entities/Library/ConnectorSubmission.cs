using Flowamaz.Core.Entities;

namespace Flowamaz.Core.Entities.Library;

/// <summary>
/// A community connector submission. The manifest is validated, a PR is opened against
/// flowamaz-io/connectors, and the submission tracks the review lifecycle.
/// </summary>
public class ConnectorSubmission : BaseEntity
{
    public Guid OrgId { get; set; }
    public string ConnectorName { get; set; } = string.Empty;
    public string ManifestYaml { get; set; } = string.Empty;
    public string? GithubPrUrl { get; set; }

    /// <summary>
    /// "pending" (pull request open), "merged" (waiting for the Library sync), "live" (in the Library) or
    /// "rejected" (pull request closed without merging). "approved" is the legacy spelling of merged.
    /// </summary>
    public string Status { get; set; } = "pending";

    public string? ReviewerNotes { get; set; }

    /// <summary>The folder name (the manifest's id), so the submission can be matched to the live connector.</summary>
    public string? ConnectorId { get; set; }

    /// <summary>The publisher id the pull request used (the org slug); null on submissions made before it was stored.</summary>
    public string? PublisherId { get; set; }

    /// <summary>When the pull request's state was last read from GitHub.</summary>
    public DateTime? PrCheckedAt { get; set; }

    /// <summary>When it was merged, closed or went live.</summary>
    public DateTime? DecidedAt { get; set; }
}
