namespace Flowamaz.Core.Models;

/// <summary>
/// Boolean feature flags for a plan (FUNCTIONAL.md §13.2 / §1.6). Stored as a jsonb column
/// on <see cref="Entities.Platform.Plan"/>.
/// </summary>
public sealed class PlanFeatures
{
    public bool Sso { get; set; }
    public bool Scim { get; set; }
    public bool CustomConnectors { get; set; }
    /// <summary>The audit log itself (every plan; how far back is <see cref="PlanLimits.AuditHistoryDays"/>).</summary>
    public bool AuditLog { get; set; }

    /// <summary>CSV export of the audit log and the organisation-wide audit view (paid plans).</summary>
    public bool AuditExport { get; set; }
    public bool ApiAccess { get; set; }
    public bool Byom { get; set; }
    public bool DataResidency { get; set; }
    public bool Cmek { get; set; }
    public bool ProcessIntelligence { get; set; }
    public bool RoiAnalytics { get; set; }
}
