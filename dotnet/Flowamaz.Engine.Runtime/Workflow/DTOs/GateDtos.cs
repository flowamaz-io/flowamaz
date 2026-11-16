namespace Flowamaz.Application.Workflow.DTOs;

public sealed record GateResponse(
    Guid Id, Guid InstanceId, string NodeId, string Decision, string? AssignedToEmail,
    string DeliveryChannel, string DeliveryStatus, DateTime? ExpiresAt, DateTime CreatedAt,
    DateTime? DecidedAt, string? DecisionNote, bool IsTest = false, string? DecidedByLabel = null,
    // The form the approver fills in (empty when the gate has none) and, once approved, the answers.
    IReadOnlyList<Flowamaz.Core.Workflow.GateFormField>? Form = null,
    System.Text.Json.JsonElement? FormValues = null,
    // What the approver sees as the gate's name: the node's config.gate_label, else its label (null when
    // neither is set or the run's version can't be read; the UI then humanises NodeId).
    string? GateLabel = null,
    string? Label = null);

/// <summary>
/// Minimal gate metadata returned by cross-workspace gate lookups (email/Slack click-through).
/// Not scoped to a workspace — callers must verify the signature before trusting it.
/// </summary>
public sealed record GateInfoDto(
    Guid Id,
    Guid WorkspaceId,
    Guid InstanceId,
    string NodeId,
    DateTime? ExpiresAt);
