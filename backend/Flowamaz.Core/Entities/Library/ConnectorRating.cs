using Flowamaz.Core.Entities;

namespace Flowamaz.Core.Entities.Library;

/// <summary>
/// One org's rating + optional review of a connector. Unique per (connector, org) — a repeat
/// rating from the same org updates the existing row (upsert).
/// </summary>
public class ConnectorRating : BaseEntity
{
    public Guid ConnectorDefinitionId { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>1–5 stars.</summary>
    public int Rating { get; set; }

    /// <summary>Optional review text, max 500 chars.</summary>
    public string? Review { get; set; }
}
