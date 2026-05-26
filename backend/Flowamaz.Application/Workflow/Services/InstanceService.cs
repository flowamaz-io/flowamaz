using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Services;

/// <summary>
/// Read models + retry for workflow instances. Sensitive variable values are always masked as
/// "***" before leaving the service. Trigger/cancel live on the orchestrator; this service owns
/// the query side and the "retry = fresh instance from the same pinned version" operation.
/// </summary>
public sealed class InstanceService
{
    private const string Masked = "***";

    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowNodeStateRepository _nodeStates;
    private readonly IWorkflowVariableRepository _variables;
    private readonly IWorkflowEventRepository _events;
    private readonly ITaskQueue _queue;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InstanceService> _logger;

    public InstanceService(
        IWorkflowInstanceRepository instances,
        IWorkflowNodeStateRepository nodeStates,
        IWorkflowVariableRepository variables,
        IWorkflowEventRepository events,
        ITaskQueue queue,
        IUnitOfWork unitOfWork,
        ILogger<InstanceService> logger)
    {
        _instances = instances;
        _nodeStates = nodeStates;
        _variables = variables;
        _events = events;
        _queue = queue;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<List<InstanceListItem>> ListAsync(
        Guid workspaceId, InstanceStatus? status, Guid? workflowDefinitionId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var instances = await _instances.GetForWorkspaceAsync(workspaceId, ct);
        return instances
            .Where(i => status is null || i.Status == status)
            .Where(i => workflowDefinitionId is null || i.WorkflowDefinitionId == workflowDefinitionId)
            .Where(i => from is null || i.CreatedAt >= from)
            .Where(i => to is null || i.CreatedAt <= to)
            .Select(i => new InstanceListItem(
                i.Id, i.WorkflowDefinitionId, i.Status.ToString(), i.TriggerType.ToString(),
                i.StartedAt, i.CompletedAt, i.CreatedAt))
            .ToList();
    }

    public async Task<InstanceDetailResponse?> GetDetailAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var instance = await _instances.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (instance is null) return null;

        var states = await _nodeStates.GetForInstanceAsync(id, ct);
        var variables = await _variables.GetForInstanceAsync(id, ct);
        var events = await _events.GetForInstanceAsync(id, ct);

        return new InstanceDetailResponse(
            instance.Id,
            instance.WorkflowDefinitionId,
            instance.WorkflowVersionId,
            instance.Status.ToString(),
            instance.TriggerType.ToString(),
            instance.CorrelationId,
            instance.StartedAt,
            instance.CompletedAt,
            instance.FailedAt,
            instance.ErrorMessage,
            instance.CurrentNodeId,
            states.Select(ToNodeState).ToList(),
            variables.Select(ToVariable).ToList(),
            events.TakeLast(50).Select(ToEvent).ToList());
    }

    public async Task<List<EventResponse>?> GetEventsAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var instance = await _instances.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (instance is null) return null;
        var events = await _events.GetForInstanceAsync(id, ct);
        return events.Select(ToEvent).ToList();
    }

    public async Task<List<VariableResponse>?> GetVariablesAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var instance = await _instances.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (instance is null) return null;
        var variables = await _variables.GetForInstanceAsync(id, ct);
        return variables.Select(ToVariable).ToList();
    }

    public async Task<List<TimelineEntry>?> GetTimelineAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var instance = await _instances.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (instance is null) return null;
        var states = await _nodeStates.GetForInstanceAsync(id, ct);
        return states
            .OrderBy(s => s.StartedAt ?? s.CreatedAt)
            .Select(s => new TimelineEntry(s.NodeId, s.NodeType, s.Status.ToString(), s.StartedAt, s.CompletedAt))
            .ToList();
    }

    public async Task<TriggerInstanceResponse?> RetryAsync(Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("InstanceService.RetryAsync enter workspace={WorkspaceId} instance={InstanceId}", workspaceId, id);

        var original = await _instances.GetByIdForWorkspaceAsync(id, workspaceId, ct);
        if (original is null) return null;

        var retry = new WorkflowInstance
        {
            WorkspaceId = workspaceId,
            WorkflowDefinitionId = original.WorkflowDefinitionId,
            WorkflowVersionId = original.WorkflowVersionId, // same pinned version
            Status = InstanceStatus.Pending,
            TriggerType = original.TriggerType,
            TriggerPayload = original.TriggerPayload,
            CorrelationId = original.CorrelationId,
        };

        await _instances.AddAsync(retry, ct);
        await _events.AppendAsync(new WorkflowEvent
        {
            WorkspaceId = workspaceId,
            InstanceId = retry.Id,
            SequenceNumber = 1,
            EventType = "InstanceStarted",
            Payload = $$"""{"retryOf":"{{id}}"}""",
            OccurredAt = DateTime.UtcNow,
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _queue.EnqueueAsync(workspaceId, retry.Id, ct);

        _logger.LogInformation("InstanceService.RetryAsync exit newInstance={InstanceId}", retry.Id);
        return new TriggerInstanceResponse(retry.Id, retry.Status.ToString(), retry.CreatedAt);
    }

    private static NodeStateResponse ToNodeState(WorkflowNodeState s) => new(
        s.NodeId, s.NodeType, s.Status.ToString(), s.StartedAt, s.CompletedAt, s.RetryCount, s.ErrorMessage);

    private static VariableResponse ToVariable(WorkflowVariable v) => new(
        v.Name, v.IsSensitive ? Masked : v.Value, v.IsSensitive);

    private static EventResponse ToEvent(WorkflowEvent e) => new(
        e.SequenceNumber, e.EventType, e.NodeId, e.NodeType, e.Payload, e.OccurredAt);
}
