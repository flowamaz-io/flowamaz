using Flowamaz.Core.Entities;

namespace Flowamaz.Core.Entities.Library;

/// <summary>
/// A reusable workflow template shown in the template gallery. Official templates
/// (<see cref="IsOfficial"/> = true) have <see cref="OrgId"/> = null and are seeded; community
/// templates are published from a workspace's workflow and carry the publishing org id. The
/// <see cref="YamlContent"/> is valid SFG workflow YAML and is used verbatim to create a new
/// <c>WorkflowDefinition</c> on install.
/// </summary>
public class WorkflowTemplate : AuditableEntity
{
    /// <summary>Display name shown on the card and pre-filled into the install modal.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unique, URL-safe identifier, e.g. "purchase-approval".</summary>
    public string Slug { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Category slug: Finance | HR | IT | Legal | Operations | Custom.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>True for seeded platform templates; false for community submissions.</summary>
    public bool IsOfficial { get; set; }

    /// <summary>Null for official templates; the publishing org for community templates.</summary>
    public Guid? OrgId { get; set; }

    /// <summary>The full SFG workflow YAML (flowamaz/v1). Used verbatim to create a workflow on install.</summary>
    public string YamlContent { get; set; } = string.Empty;

    public string? PreviewImageUrl { get; set; }

    /// <summary>Number of times this template has been installed (atomically incremented).</summary>
    public int InstallCount { get; set; }

    /// <summary>Mean of all ratings (0 when none).</summary>
    public decimal AverageRating { get; set; }

    /// <summary>Searchable tags stored as jsonb string array.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>SemVer string, e.g. "1.0.0".</summary>
    public string Version { get; set; } = "1.0.0";

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Review state for community templates: "pending", "approved", "rejected".
    /// Official templates are always "approved".
    /// </summary>
    public string ReviewStatus { get; set; } = "approved";
}
