namespace Flowamaz.Core.Models;

/// <summary>
/// Quantitative caps for a plan (FUNCTIONAL.md §13.2). Stored as a jsonb column on
/// <see cref="Entities.Platform.Plan"/>. A value of 0 on a Max* field means "unlimited".
/// </summary>
public sealed class PlanLimits
{
    public int MaxWorkspaces { get; set; }
    public int MaxMembersPerWorkspace { get; set; }
    public int MaxWorkflowDefinitions { get; set; }
    public long MaxRunsPerMonth { get; set; }
    public long MaxAiCallsPerMonth { get; set; }
    public int MaxStorageGb { get; set; }
    public int RunRetentionDays { get; set; }
    /// <summary>Public API calls a minute across the organisation (an organisation's own value overrides it).</summary>
    public int ApiRateLimitPerMinute { get; set; }

    /// <summary>
    /// How far back the audit log can be read, in days (0 = all of it). Older events are kept, not deleted, so
    /// upgrading shows them again.
    /// </summary>
    public int AuditHistoryDays { get; set; }
    public List<string> AllowedAiProviders { get; set; } = [];
}
