using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Api.Controllers.Public;

/// <summary>Public-API projections — only fields safe and useful for external developers,
/// serialized with snake_case names by <see cref="PublicApiController.PublicJson"/>.</summary>
public sealed record PublicWorkflow(
    Guid Id, string Name, string Slug, string Status, string? Description, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static PublicWorkflow From(WorkflowDefinition w) =>
        new(w.Id, w.Name, w.Slug, w.Status.ToString(), w.Description, w.CreatedAt, w.UpdatedAt);
}

public sealed record PublicInstance(
    Guid Id, Guid WorkflowDefinitionId, string Status, string TriggerType, string? CorrelationId,
    DateTime? StartedAt, DateTime? CompletedAt, DateTime CreatedAt)
{
    public static PublicInstance From(WorkflowInstance i) =>
        new(i.Id, i.WorkflowDefinitionId, i.Status.ToString(), i.TriggerType.ToString(), i.CorrelationId,
            i.StartedAt, i.CompletedAt, i.CreatedAt);
}

public sealed record PublicEvent(
    Guid Id, long Sequence, string EventType, string? NodeId, string? NodeType, DateTime OccurredAt)
{
    public static PublicEvent From(WorkflowEvent e) =>
        new(e.Id, e.SequenceNumber, e.EventType, e.NodeId, e.NodeType, e.OccurredAt);
}
