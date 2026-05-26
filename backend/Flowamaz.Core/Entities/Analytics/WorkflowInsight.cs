using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Analytics;

/// <summary>
/// An actionable insight surfaced by Process Intelligence (SLA risk, bottleneck, anomaly…).
/// Unacknowledged insights of the same type for the same workflow are replaced on each run so the
/// list never accumulates duplicates.
/// </summary>
public class WorkflowInsight : WorkspaceEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public Guid? InstanceId { get; set; }
    public InsightType InsightType { get; set; }
    public InsightSeverity Severity { get; set; } = InsightSeverity.Info;
    public string Message { get; set; } = string.Empty;

    /// <summary>Supporting data as a jsonb document.</summary>
    public string Data { get; set; } = "{}";

    public bool IsAcknowledged { get; set; }
    public Guid? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
