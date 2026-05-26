namespace Flowamaz.Core.Entities.Analytics;

/// <summary>
/// Per-workflow, per-hour rollup produced by the Process Intelligence job. One row per
/// (workflow, hour). Powers Workflow Weather and feeds the AI insight generator.
/// </summary>
public class WorkflowMetric : WorkspaceEntity
{
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>The hour this rollup covers, truncated to the top of the hour (UTC).</summary>
    public DateTime PeriodHour { get; set; }

    public int RunsTotal { get; set; }
    public int RunsCompleted { get; set; }
    public int RunsFailed { get; set; }
    public int RunsCancelled { get; set; }

    public long AvgDurationMs { get; set; }
    public long P95DurationMs { get; set; }
    public long P99DurationMs { get; set; }

    public int SlaBreachCount { get; set; }
    public long? SlaThresholdMs { get; set; }

    public string? BottleneckNodeId { get; set; }
    public long? BottleneckAvgMs { get; set; }
}
