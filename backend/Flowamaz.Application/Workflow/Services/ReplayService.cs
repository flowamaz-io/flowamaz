using System.Text.Json;
using System.Text.Json.Nodes;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Services;

/// <summary>
/// Replays a terminal instance as a fresh TEST run (prompt 07-04). Reuses the same instance-create +
/// enqueue path the orchestrator uses on trigger (Pending instance → InstanceStarted event →
/// task queue), but pins the original version, merges payloads and stamps lineage. A replay is
/// ALWAYS a test run — it never consumes the production run budget and never targets production.
/// </summary>
public sealed class ReplayService : IReplayService
{
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowEventRepository _events;
    private readonly ITaskQueue _queue;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReplayService> _logger;

    public ReplayService(
        IWorkflowInstanceRepository instances,
        IWorkflowEventRepository events,
        ITaskQueue queue,
        IUnitOfWork unitOfWork,
        ILogger<ReplayService> logger)
    {
        _instances = instances;
        _events = events;
        _queue = queue;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Guid?> ReplayInstanceAsync(
        Guid instanceId, string? payloadOverride, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "ReplayService.ReplayInstanceAsync enter workspace={WorkspaceId} instance={InstanceId}",
            workspaceId, instanceId);

        try
        {
            var original = await _instances.GetByIdForWorkspaceAsync(instanceId, workspaceId, cancellationToken);
            if (original is null)
            {
                _logger.LogInformation(
                    "ReplayService.ReplayInstanceAsync not-found workspace={WorkspaceId} instance={InstanceId}",
                    workspaceId, instanceId);
                return null;
            }

            if (original.Status is not (InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled))
            {
                throw new InstanceNotReplayableException(instanceId, original.Status.ToString());
            }

            var mergedPayload = MergePayload(original.TriggerPayload, payloadOverride);

            var replay = new WorkflowInstance
            {
                WorkspaceId = workspaceId,
                WorkflowDefinitionId = original.WorkflowDefinitionId,
                WorkflowVersionId = original.WorkflowVersionId, // same pinned version
                EnvironmentType = original.EnvironmentType,
                Status = InstanceStatus.Pending,
                TriggerType = original.TriggerType,
                TriggerPayload = mergedPayload,
                CorrelationId = original.CorrelationId,
                IsTest = true,                       // replay is always a test run
                TestExpiresAt = DateTime.UtcNow.AddHours(24),
                ParentInstanceId = original.Id,      // lineage
            };

            await _instances.AddAsync(replay, cancellationToken);
            await _events.AppendAsync(new WorkflowEvent
            {
                WorkspaceId = workspaceId,
                InstanceId = replay.Id,
                SequenceNumber = 1,
                EventType = "InstanceStarted",
                Payload = mergedPayload ?? "{}",
                OccurredAt = DateTime.UtcNow,
            }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _queue.EnqueueAsync(workspaceId, replay.Id, cancellationToken);

            _logger.LogInformation(
                "ReplayService.ReplayInstanceAsync exit parent={ParentInstanceId} newInstance={InstanceId}",
                instanceId, replay.Id);
            return replay.Id;
        }
        catch (Exception ex) when (ex is not AppException and not OperationCanceledException)
        {
            _logger.LogError(ex,
                "ReplayService.ReplayInstanceAsync error workspace={WorkspaceId} instance={InstanceId}",
                workspaceId, instanceId);
            throw;
        }
    }

    /// <summary>
    /// Shallow-merges the override object onto the original payload: override keys win, original keys
    /// are preserved. Non-object payloads (or a null original) fall back to whichever side is present.
    /// </summary>
    private static string? MergePayload(string? original, string? @override)
    {
        if (string.IsNullOrWhiteSpace(@override)) return original;
        if (string.IsNullOrWhiteSpace(original)) return @override;

        if (TryParseObject(original) is { } baseObj && TryParseObject(@override) is { } overObj)
        {
            foreach (var kv in overObj)
            {
                baseObj[kv.Key] = kv.Value?.DeepClone();
            }
            return baseObj.ToJsonString();
        }

        // One side is not an object — the override replaces the original wholesale.
        return @override;
    }

    private static JsonObject? TryParseObject(string raw)
    {
        try { return JsonNode.Parse(raw) as JsonObject; }
        catch (JsonException) { return null; }
    }
}
