namespace Flowamaz.Core.Entities.Platform;

/// <summary>
/// Per-org, per-month rollup of billable usage (FUNCTIONAL.md §13.1). One row per
/// (OrgId, PeriodMonth). Counts only — never any workflow content. Updated by the async
/// metering side-channel; created here so the schema exists from Phase 1.
/// </summary>
public class UsageAggregate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }

    /// <summary>Billing period in "yyyy-MM" form, e.g. "2026-05".</summary>
    public string PeriodMonth { get; set; } = string.Empty;
    public long RunsCount { get; set; }
    public long AiCallsCount { get; set; }
    public decimal AiCostUsd { get; set; }
    public long StorageBytes { get; set; }
    public int MemberPeak { get; set; }
    public long ApiCallsCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
