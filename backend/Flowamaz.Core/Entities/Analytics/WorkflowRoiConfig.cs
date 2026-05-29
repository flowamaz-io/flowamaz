namespace Flowamaz.Core.Entities.Analytics;

/// <summary>
/// Admin-set financial baseline for a workflow (FUNCTIONAL.md / prompt 05-04). Combined with actual
/// run counts from <see cref="WorkflowMetric"/> it yields deterministic time-saved / cost-avoided /
/// ROI figures — no AI, no estimation. One row per workflow, workspace-scoped.
/// </summary>
public class WorkflowRoiConfig : WorkspaceEntity
{
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>How long this process took a human before automation (minutes per run).</summary>
    public int ManualProcessTimeMinutes { get; set; }

    /// <summary>Fully-loaded human cost of one manual run.</summary>
    public decimal ManualProcessCostPerRunUsd { get; set; }

    /// <summary>Automated cost per run — connector + AI + platform.</summary>
    public decimal AutomationCostPerRunUsd { get; set; }

    /// <summary>Display currency for the figures. Default MYR.</summary>
    public string MonthlyCurrency { get; set; } = "MYR";
}
