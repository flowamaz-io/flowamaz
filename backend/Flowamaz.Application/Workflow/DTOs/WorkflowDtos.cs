using Flowamaz.Core.Enums;

namespace Flowamaz.Application.Workflow.DTOs;

// ── Requests ────────────────────────────────────────────────────────────────

public sealed record CreateWorkflowDefinitionRequest(
    string Name,
    string Slug,
    string YamlContent,
    string? NlDescription,
    WorkflowCreatedByMethod CreatedByMethod,
    long? SlaThresholdMs = null);

public sealed record UpdateWorkflowDefinitionRequest(string YamlContent, long? SlaThresholdMs = null);

public sealed record TriggerInstanceRequest(Guid WorkflowDefinitionId, string? Payload, string? IdempotencyKey, bool IsTest = false);

public sealed record GateDecisionRequest(string Decision, string? Note);

// ── Responses ───────────────────────────────────────────────────────────────

public sealed record WorkflowDefinitionListItem(
    Guid Id, string Name, string Slug, WorkflowStatus Status, int HealthScore, DateTime UpdatedAt);

public sealed record WorkflowDefinitionResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Slug,
    string? Description,
    string YamlContent,
    string? NlDescription,
    WorkflowCreatedByMethod CreatedByMethod,
    WorkflowStatus Status,
    string CurrentVersion,
    int HealthScore,
    WorkflowTriggerType TriggerType,
    long? SlaThresholdMs,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WorkflowVersionResponse(
    Guid Id, Guid WorkflowDefinitionId, string CommitSha, string? TagName,
    string BranchName, string Message, bool IsProduction, DateTime CreatedAt);

/// <summary>Workflow YAML reconstructed from a specific Git commit (read-only history view).</summary>
public sealed record WorkflowAtCommitResponse(Guid WorkflowId, string CommitSha, string YamlContent);

public sealed record TriggerInstanceResponse(Guid InstanceId, string Status, DateTime TriggeredAt);

public sealed record InstanceListItem(
    Guid Id, Guid WorkflowDefinitionId, string Status, string TriggerType,
    DateTime? StartedAt, DateTime? CompletedAt, DateTime CreatedAt, bool IsTest, DateTime? TestExpiresAt);

public sealed record NodeStateResponse(
    string NodeId, string NodeType, string Status,
    DateTime? StartedAt, DateTime? CompletedAt, int RetryCount, string? ErrorMessage);

public sealed record VariableResponse(string Name, string Value, bool IsSensitive);

public sealed record EventResponse(
    long SequenceNumber, string EventType, string? NodeId, string? NodeType, string Payload, DateTime OccurredAt);

public sealed record InstanceDetailResponse(
    Guid Id,
    Guid WorkflowDefinitionId,
    Guid WorkflowVersionId,
    string Status,
    string TriggerType,
    string? CorrelationId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? FailedAt,
    string? ErrorMessage,
    string? CurrentNodeId,
    IReadOnlyList<NodeStateResponse> NodeStates,
    IReadOnlyList<VariableResponse> Variables,
    IReadOnlyList<EventResponse> Events);

public sealed record GateResponse(
    Guid Id, Guid InstanceId, string NodeId, string Decision, string? AssignedToEmail,
    string DeliveryChannel, string DeliveryStatus, DateTime? ExpiresAt, DateTime CreatedAt,
    DateTime? DecidedAt, string? DecisionNote);

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

// ── Run timeline (waterfall, prompt 02-05) ───────────────────────────────────

public sealed record TimelineResponse(
    Guid InstanceId,
    long TotalDurationMs,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<TimelineNode> Nodes);

public sealed record TimelineNode(
    string NodeId,
    string NodeType,
    string Label,
    string Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    long DurationMs,
    long OffsetMs,
    int RetryCount,
    bool HasOutput);

// ── Step debugger requests (prompt 02-05) ─────────────────────────────────────

public sealed record PauseRequest(string AfterNodeId);

public sealed record ForceVariableRequest(string Name, string Value);

public sealed record ForceBranchRequest(string RouterNodeId, string TargetBranchEdgeId);
