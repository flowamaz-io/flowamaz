using System.Globalization;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Interpreter;

/// <summary>
/// Generates CEO / Auditor / Developer narratives of a run (FUNCTIONAL.md §8.10). Only the CEO
/// narrative calls AI (F5 process-intel, resolved via <see cref="IModelResolutionService"/> and
/// metered); Auditor and Developer are built deterministically from the event log / node states.
/// </summary>
public sealed class WorkflowInterpreterService : IWorkflowInterpreterService
{
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowNodeStateRepository _nodeStates;
    private readonly IWorkflowVariableRepository _variables;
    private readonly IWorkflowEventRepository _events;
    private readonly IModelResolutionService _modelResolution;
    private readonly IAiCompletionService _completion;
    private readonly IAiTokenMeteringService _metering;
    private readonly ILogger<WorkflowInterpreterService> _logger;

    public WorkflowInterpreterService(
        IWorkflowInstanceRepository instances,
        IWorkflowNodeStateRepository nodeStates,
        IWorkflowVariableRepository variables,
        IWorkflowEventRepository events,
        IModelResolutionService modelResolution,
        IAiCompletionService completion,
        IAiTokenMeteringService metering,
        ILogger<WorkflowInterpreterService> logger)
    {
        _instances = instances;
        _nodeStates = nodeStates;
        _variables = variables;
        _events = events;
        _modelResolution = modelResolution;
        _completion = completion;
        _metering = metering;
        _logger = logger;
    }

    public async Task<InterpreterNarrative?> GenerateNarrativeAsync(
        Guid workspaceId, Guid instanceId, NarrativeAudience audience, CancellationToken cancellationToken = default)
    {
        var instance = await _instances.GetByIdForWorkspaceAsync(instanceId, workspaceId, cancellationToken);
        if (instance is null) return null;

        _logger.LogInformation(
            "WorkflowInterpreterService.GenerateNarrativeAsync instance={InstanceId} audience={Audience}", instanceId, audience);

        return audience switch
        {
            NarrativeAudience.Ceo => await BuildCeoAsync(instance, cancellationToken),
            NarrativeAudience.Auditor => await BuildAuditorAsync(instance, cancellationToken),
            _ => await BuildDeveloperAsync(instance, cancellationToken),
        };
    }

    private async Task<InterpreterNarrative> BuildCeoAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var variables = await _variables.GetForInstanceAsync(instance.Id, ct);
        var facts = variables.Count == 0
            ? "(no variables recorded yet)"
            : string.Join("\n", variables.Select(v => $"- {v.Name}: {ExtractScalar(v.Value)}"));

        var systemPrompt =
            "You are an executive assistant. In 2-3 sentences of plain English with no technical jargon, " +
            "summarise this workflow run's current status using the actual values, and state the expected next step.";
        var userPrompt = $"The workflow run is currently '{instance.Status}'.\nKey values:\n{facts}";

        var config = await _modelResolution.ResolveModelConfigAsync(AiFunctionIds.ProcessIntel, instance.WorkspaceId, ct);
        var result = await _completion.CompleteAsync(config, systemPrompt, userPrompt, ct);

        // Mandatory metering on every AI call (CLAUDE.md §3). Phase-2 local completion → 0 cost.
        _metering.RecordUsage(
            AiFunctionIds.ProcessIntel, config.ModelId, config.Provider,
            orgId: null, workspaceId: instance.WorkspaceId,
            tokensInput: result.TokensInput, tokensOutput: result.TokensOutput, costUsd: 0m);

        return new InterpreterNarrative("ceo", result.Text, DateTime.UtcNow);
    }

    private async Task<InterpreterNarrative> BuildAuditorAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var events = await _events.GetForInstanceAsync(instance.Id, ct);
        var variables = await _variables.GetForInstanceAsync(instance.Id, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"# Compliance Record — Instance {instance.Id}");
        sb.AppendLine();
        sb.AppendLine($"Status: **{instance.Status}**  ");
        sb.AppendLine($"Started: {Format(instance.StartedAt)}  Completed: {Format(instance.CompletedAt)}");
        sb.AppendLine();
        sb.AppendLine("## Event log");
        foreach (var e in events)
        {
            var node = string.IsNullOrEmpty(e.NodeId) ? "" : $" — node `{e.NodeId}`";
            sb.AppendLine($"- {Format(e.OccurredAt)} — **{e.EventType}**{node}");
        }

        sb.AppendLine();
        sb.AppendLine("## Recorded values");
        foreach (var v in variables)
        {
            sb.AppendLine($"- {v.Name}: {(v.IsSensitive ? "[redacted]" : ExtractScalar(v.Value))}");
        }

        return new InterpreterNarrative("auditor", sb.ToString(), DateTime.UtcNow);
    }

    private async Task<InterpreterNarrative> BuildDeveloperAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var states = (await _nodeStates.GetForInstanceAsync(instance.Id, ct))
            .OrderBy(s => s.StartedAt ?? s.CreatedAt)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Technical Execution Log — Instance {instance.Id}");
        sb.AppendLine($"Version: {instance.WorkflowVersionId}  Status: {instance.Status}");
        sb.AppendLine();
        foreach (var s in states)
        {
            var durationMs = s.StartedAt is not null && s.CompletedAt is not null
                ? (long)(s.CompletedAt.Value - s.StartedAt.Value).TotalMilliseconds
                : 0;
            sb.AppendLine($"## node `{s.NodeId}` ({s.NodeType})");
            sb.AppendLine($"- status: {s.Status}");
            sb.AppendLine($"- started_at: {Format(s.StartedAt)}");
            sb.AppendLine($"- duration_ms: {durationMs}");
            sb.AppendLine($"- retry_count: {s.RetryCount}");
            sb.AppendLine($"- has_input: {s.InputPayload is not null}  has_output: {s.OutputPayload is not null}");
            if (!string.IsNullOrEmpty(s.ErrorMessage)) sb.AppendLine($"- error: {s.ErrorMessage}");
            sb.AppendLine();
        }

        return new InterpreterNarrative("developer", sb.ToString(), DateTime.UtcNow);
    }

    private static string Format(DateTime? value) =>
        value is null ? "—" : value.Value.ToString("u", CultureInfo.InvariantCulture);

    private static string ExtractScalar(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString() ?? string.Empty
                : doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
