using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Workflow;

/// <summary>
/// A human approval gate for a node. Created when execution reaches a gate node; resolved when
/// the assignee approves/rejects or the timer scanner escalates an expired gate.
/// </summary>
public class GateDecision : WorkspaceEntity
{
    public Guid InstanceId { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public Guid? AssignedTo { get; set; }
    public string? AssignedToEmail { get; set; }
    public GateDecisionStatus Decision { get; set; } = GateDecisionStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public GateDeliveryChannel DeliveryChannel { get; set; } = GateDeliveryChannel.Portal;
    public GateDeliveryStatus DeliveryStatus { get; set; } = GateDeliveryStatus.Pending;
    public string? DeliveryError { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Guid? EscalatedTo { get; set; }
}
