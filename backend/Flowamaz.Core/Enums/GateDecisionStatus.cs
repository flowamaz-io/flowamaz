namespace Flowamaz.Core.Enums;

/// <summary>
/// Outcome of a human approval gate. Named *Status to avoid colliding with the
/// <c>GateDecision</c> entity. Stored as a string column.
/// </summary>
public enum GateDecisionStatus
{
    Pending,
    Approved,
    Rejected,
    Escalated,
}
