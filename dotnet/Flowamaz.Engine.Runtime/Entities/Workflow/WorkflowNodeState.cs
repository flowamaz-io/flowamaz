using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Workflow;

/// <summary>
/// Current state snapshot for a single node in an instance — mutable, for fast reads.
/// The authoritative history lives in <see cref="WorkflowEvent"/>; this is a denormalised view.
/// </summary>
public class WorkflowNodeState : WorkspaceEntity
{
    public Guid InstanceId { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public NodeStatus Status { get; set; } = NodeStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? InputPayload { get; set; }
    public string? OutputPayload { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime? LastRetryAt { get; set; }
}
