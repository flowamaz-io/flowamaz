namespace Flowamaz.Core.Entities.Workflow;

/// <summary>
/// Append-only audit record of everything that happens to an instance. This table is immutable:
/// it intentionally does NOT inherit <see cref="BaseEntity"/> (no soft delete, no UpdatedAt) and
/// the repository exposes insert/read only — never update or delete. Replay of these rows in
/// <see cref="SequenceNumber"/> order reconstructs an instance's full history.
/// </summary>
public class WorkflowEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid InstanceId { get; set; }

    /// <summary>Monotonically increasing per instance, starting at 1.</summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// One of: NodeStarted, NodeCompleted, NodeFailed, InstanceStarted, InstanceCompleted,
    /// InstanceFailed, GateOpened, GateDecided, CompensationStarted, CompensationCompleted.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    public string? NodeId { get; set; }
    public string? NodeType { get; set; }

    /// <summary>Inputs, outputs, or error details as a jsonb document.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>When the event actually happened (UTC, precise).</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>When the row was written to the database.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
