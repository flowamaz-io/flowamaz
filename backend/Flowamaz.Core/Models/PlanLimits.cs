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
    public int ApiRateLimitPerMinute { get; set; }
    public List<string> AllowedAiProviders { get; set; } = [];
}
