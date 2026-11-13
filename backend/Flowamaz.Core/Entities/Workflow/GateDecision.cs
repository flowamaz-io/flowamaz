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

    /// <summary>The form the approver fills in (the gate's config.form.fields as JSON), or null when it has none.</summary>
    public string? FormJson { get; set; }

    /// <summary>The approver's checked answers (JSON object keyed by field key), set on approval.</summary>
    public string? FormValuesJson { get; set; }
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

    /// <summary>
    /// The timeout a gate's config sets, in hours, or null when it sets none (the engine then uses
    /// <see cref="DefaultTimeoutHours"/>). Reads every form the engine accepts: <c>timeout_hours</c> (spec),
    /// <c>timeoutHours</c>, and the canvas's <c>timeoutValue</c> + <c>timeoutUnit</c> (hours or days).
    /// </summary>
    public static int? ReadTimeoutHours(System.Text.Json.JsonElement config)
    {
        if (config.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        var hours = Int(config, "timeout_hours") ?? Int(config, "timeoutHours");
        if (hours is > 0) return hours;
        var value = Int(config, "timeoutValue");
        if (value is null or <= 0) return null;
        return config.TryGetProperty("timeoutUnit", out var unit) && unit.ValueKind == System.Text.Json.JsonValueKind.String
               && string.Equals(unit.GetString(), "days", StringComparison.OrdinalIgnoreCase) ? value * 24 : value;
    }

    /// <summary>The timeout action a gate's config sets (<c>on_timeout</c> or <c>onTimeout</c>), as written, or null.</summary>
    public static string? ReadOnTimeout(System.Text.Json.JsonElement config) =>
        Str(config, "on_timeout") ?? Str(config, "onTimeout");

    /// <summary>The escalation email a gate's config sets (<c>escalate_to</c> or <c>escalateTo</c>), or null.</summary>
    public static string? ReadEscalateTo(System.Text.Json.JsonElement config) =>
        Str(config, "escalate_to") ?? Str(config, "escalateTo");

    private static string? Str(System.Text.Json.JsonElement e, string key) =>
        e.ValueKind == System.Text.Json.JsonValueKind.Object && e.TryGetProperty(key, out var v)
        && v.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

    private static int? Int(System.Text.Json.JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) ? v.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            System.Text.Json.JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
            _ => null,
        } : null;
}
