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

    /// <summary>"pending", "approved", or "rejected".</summary>
    public string Status { get; set; } = "pending";

    public string? ReviewerNotes { get; set; }
}
