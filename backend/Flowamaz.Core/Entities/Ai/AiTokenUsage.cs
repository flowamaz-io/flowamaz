namespace Flowamaz.Core.Entities.Ai;

/// <summary>
/// Immutable metering record for every AI call. Recorded fire-and-forget by
/// <c>IAiTokenMeteringService</c> — never blocks the request path.
/// One row per AI call. Used for billing rollup, cost dashboards, anomaly detection.
/// </summary>
public class AiTokenUsage : BaseEntity
{
    public Guid? OrgId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string FunctionId { get; set; } = string.Empty; // see Constants.AiFunctionIds
    public string ModelId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;   // see Constants.AiProviders
    public int TokensInput { get; set; }
    public int TokensOutput { get; set; }
    public decimal CostUsd { get; set; }
}
