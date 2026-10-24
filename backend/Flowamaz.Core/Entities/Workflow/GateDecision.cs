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

    /// <summary>
    /// Who decided, as named by the external application that called the public API
    /// (<c>decided_by</c>), e.g. "Aisha Rahman (Branch Manager)". Null for decisions made by a
    /// Flowamaz user (see <see cref="DecidedBy"/>) or by a timeout.
    /// </summary>
    public string? DecidedByLabel { get; set; }

    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public GateDeliveryChannel DeliveryChannel { get; set; } = GateDeliveryChannel.Portal;
    public GateDeliveryStatus DeliveryStatus { get; set; } = GateDeliveryStatus.Pending;
    public string? DeliveryError { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Guid? EscalatedTo { get; set; }

    /// <summary>What happens when the gate expires: fail (default), escalate, auto-approve, auto-reject.</summary>
    public string OnTimeout { get; set; } = GateTimeoutActions.Fail;

    /// <summary>Who gets the approval when it escalates on timeout (email).</summary>
    public string? EscalateToEmail { get; set; }

    /// <summary>Original decision window, reused as the escalation window.</summary>
    public int? TimeoutHours { get; set; }

    /// <summary>Times this gate has been escalated (escalation happens at most once).</summary>
    public int EscalationCount { get; set; }
}

/// <summary>Field limits for <see cref="GateDecision"/> shared by the EF mapping and request validators.</summary>
public static class GateDecisionLimits
{
    public const int DecidedByMaxLength = 200;
    public const int NoteMaxLength = 2000;
}

/// <summary>Allowed values for <see cref="GateDecision.OnTimeout"/>.</summary>
public static class GateTimeoutActions
{
    /// <summary>Deadline applied when a gate sets no timeout (engine and validator share it).</summary>
    public const int DefaultTimeoutHours = 48;

    public const string Fail = "fail";
    public const string Escalate = "escalate";
    public const string AutoApprove = "auto-approve";
    public const string AutoReject = "auto-reject";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant().Replace('_', '-') switch
    {
        "escalate" => Escalate,
        "auto-approve" or "approve" => AutoApprove,
        "auto-reject" or "reject" => AutoReject,
        _ => Fail,
    };
}
