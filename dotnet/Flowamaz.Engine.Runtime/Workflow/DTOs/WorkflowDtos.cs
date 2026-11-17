using Flowamaz.Core.Enums;

namespace Flowamaz.Application.Workflow.DTOs;

// Requests and responses of the workflow and run services (WorkflowService, InstanceService). Hosts use them
// as their API contract; the Flowamaz product's controllers return them unchanged.

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

/// <summary><c>Values</c>: the approver's answers to the gate's form, keyed by field key (checked on approval).</summary>
public sealed record GateDecisionRequest(string Decision, string? Note, Dictionary<string, System.Text.Json.JsonElement>? Values = null);

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

/// <summary>One published version with the YAML it was published with (for export, e.g. `fmz cloud migrate`).</summary>
public sealed record WorkflowVersionDetail(
    Guid Id, Guid WorkflowDefinitionId, string CommitSha, string? TagName,
    string BranchName, string Message, bool IsProduction, DateTime CreatedAt, string YamlContent);

public sealed record TriggerInstanceResponse(Guid InstanceId, string Status, DateTime TriggeredAt);

public sealed record InstanceListItem(
    Guid Id, Guid WorkflowDefinitionId, string Status, string TriggerType,
    DateTime? StartedAt, DateTime? CompletedAt, DateTime CreatedAt, bool IsTest, DateTime? TestExpiresAt,
    Guid? TriggeredByUserId = null, DateTime? FailedAt = null);

/// <summary>
/// What a caller may see of runs (FUNCTIONAL.md §4). Operators and up see everything. Viewers see every run without its
/// data: no variables, no step inputs/outputs, no event payloads. Runners see only the runs they started, in full.
/// </summary>
public sealed record RunAccess(bool CanSeeData, Guid? OnlyTriggeredBy)
{
    public static readonly RunAccess Full = new(true, null);
    public static readonly RunAccess WithoutData = new(false, null);
    public static RunAccess OwnRuns(Guid userId) => new(true, userId);
}

public sealed record NodeStateResponse(
    string NodeId, string NodeType, string Status,
    DateTime? StartedAt, DateTime? CompletedAt, int RetryCount, string? ErrorMessage);

public sealed record VariableResponse(string Name, string Value, bool IsSensitive);

public sealed record EventResponse(
    long SequenceNumber, string EventType, string? NodeId, string? NodeType, string Payload, DateTime OccurredAt,
    string? InputSnapshot = null, string? OutputSnapshot = null, string? ErrorSnapshot = null, int? DurationMs = null);

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
    IReadOnlyList<EventResponse> Events,
    Guid? TriggeredByUserId = null,
    bool DataHidden = false,
    bool IsTest = false);

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
