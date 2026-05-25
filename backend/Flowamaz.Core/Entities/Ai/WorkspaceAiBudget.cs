namespace Flowamaz.Core.Entities.Ai;

/// <summary>
/// Per-workspace AI token budget. Hard cap enforced at request time (FUNCTIONAL.md §5.5 lever 8).
/// Natural key: WorkspaceId (one row per workspace). No soft-delete — config row, not BaseEntity.
/// </summary>
public class WorkspaceAiBudget
{
    public Guid WorkspaceId { get; set; }
    public long MonthlyTokenLimit { get; set; }
    public long TokensUsedThisMonth { get; set; }
    public DateTime BudgetResetDate { get; set; }
    public bool IsHardCapped { get; set; } = true;
    public DateTime? AlertSentAt80Pct { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
