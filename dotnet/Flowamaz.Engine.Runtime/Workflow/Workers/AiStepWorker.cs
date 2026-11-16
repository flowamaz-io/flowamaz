using System.Text.Json;
using System.Text.RegularExpressions;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Runtime;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Workers;

/// <summary>
/// The open runtime's AI step (<c>type: ai</c>): fills the prompt from the run's variables, calls the
/// host's model provider (<see cref="IAiModelProvider"/>, the host's own key) and meters the call
/// (<see cref="IAiUsageMeter"/>, required).
/// <list type="bullet">
/// <item>Config: <c>prompt</c> (or <c>prompt_template</c>), <c>system_prompt</c>, <c>model</c> (else the
/// provider's configured default), <c>max_tokens</c> (default 1024). camelCase spellings are accepted.</item>
/// <item>Sensitive variables (stored encrypted, or named by the host) never reach the model: their
/// placeholders become <c>[SENSITIVE_REDACTED]</c> before the prompt is filled.</item>
/// <item>Output: the model's reply parsed as JSON when it is JSON, else <c>{ "text": "…" }</c>.</item>
/// <item>Every call that reaches the provider is metered, failed ones included. If the usage can't be
/// recorded the step fails: no AI call runs unmetered.</item>
/// </list>
/// The Flowamaz product has its own AI step (model policy, BYOM, vault, budgets) and doesn't use this one.
/// </summary>
public sealed class AiStepWorker : INodeWorker
{
    public const string Redacted = "[SENSITIVE_REDACTED]";
    private const int DefaultMaxTokens = 1024;

    private readonly IAiModelProvider _provider;
    private readonly IAiUsageMeter _meter;
    private readonly IVariableEvaluationService _variableEvaluation;
    private readonly IWorkflowVariableRepository _variables;
    private readonly ILogger<AiStepWorker> _logger;

    public AiStepWorker(
        IAiModelProvider provider,
        IAiUsageMeter meter,
        IVariableEvaluationService variableEvaluation,
        IWorkflowVariableRepository variables,
        ILogger<AiStepWorker> logger)
    {
        _provider = provider;
        _meter = meter;
        _variableEvaluation = variableEvaluation;
        _variables = variables;
        _logger = logger;
    }

    public NodeType SupportedType => NodeType.Ai;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        var nodeId = context.Node.Id;
        _logger.LogInformation("AiStepWorker.ExecuteAsync enter instance={InstanceId} node={NodeId}", context.InstanceId, nodeId);

        var config = context.Node.Config.RootElement;
        var model = Text(config, "model") ?? _provider.DefaultModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            return NodeExecutionResult.Fatal(
                $"AI step '{nodeId}' has no model: set `model` on the step, or a default model for the " +
                $"'{_provider.Name}' provider in the host's configuration (for Anthropic: Ai:Anthropic:Model).");
        }

        var template = Text(config, "prompt") ?? Text(config, "prompt_template") ?? Text(config, "userPrompt") ?? Text(config, "promptTemplate");
        if (string.IsNullOrWhiteSpace(template))
        {
            return NodeExecutionResult.Fatal($"AI step '{nodeId}' has no prompt. Add `prompt:` to its config.");
        }
        var maxTokens = Int(config, "max_tokens") ?? Int(config, "maxTokens") ?? DefaultMaxTokens;
        if (maxTokens <= 0) maxTokens = DefaultMaxTokens;

        // Redact before filling, so a sensitive value is never substituted in the first place.
        var sensitive = await SensitiveNamesAsync(context, cancellationToken);
        var prompt = await _variableEvaluation.InterpolateAsync(context.InstanceId, Redact(template, sensitive), cancellationToken);
        var systemTemplate = Text(config, "system_prompt") ?? Text(config, "systemPrompt");
        var system = systemTemplate is null
            ? null
            : await _variableEvaluation.InterpolateAsync(context.InstanceId, Redact(systemTemplate, sensitive), cancellationToken);
        if (sensitive.Count > 0)
        {
            _logger.LogInformation("AiStepWorker: {Count} sensitive variable(s) withheld from the model on node {NodeId}", sensitive.Count, nodeId);
        }

        AiCompletion completion;
        try
        {
            completion = await _provider.CompleteAsync(
                new AiCompletionRequest(context.WorkspaceId, model, system, prompt, maxTokens), cancellationToken);
        }
        catch (AiProviderException ex)
        {
            _logger.LogWarning(ex, "AiStepWorker: provider {Provider} failed on node {NodeId} (retryable={Retryable})", _provider.Name, nodeId, ex.Retryable);
            if (!await TryMeterAsync(context, model, 0, 0, 0m, succeeded: false, cancellationToken))
                return MeteringFailed(nodeId);
            var message = $"AI step '{nodeId}' failed: {ex.Message}";
            return ex.Retryable
                ? NodeExecutionResult.Retryable(message + " The step is retried if it has a retry policy.")
                : NodeExecutionResult.Fatal(message);
        }

        if (!await TryMeterAsync(context, completion.Model, completion.InputTokens, completion.OutputTokens,
                completion.CostUsd ?? 0m, succeeded: true, cancellationToken))
        {
            return MeteringFailed(nodeId);
        }

        var output = ToOutput(completion.Text);
        _logger.LogInformation(
            "AiStepWorker.ExecuteAsync exit instance={InstanceId} node={NodeId} model={Model} tokensIn={In} tokensOut={Out}",
            context.InstanceId, nodeId, completion.Model, completion.InputTokens, completion.OutputTokens);
        return NodeExecutionResult.Ok(output);
    }

    private async Task<bool> TryMeterAsync(
        NodeExecutionContext context, string model, int tokensIn, int tokensOut, decimal costUsd, bool succeeded,
        CancellationToken cancellationToken)
    {
        try
        {
            await _meter.RecordAsync(new AiUsageRecord(
                context.WorkspaceId, context.InstanceId, context.Node.Id, _provider.Name, model, AiFunctions.NodeExec,
                tokensIn, tokensOut, costUsd, succeeded), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AiStepWorker: couldn't record AI usage for node {NodeId}", context.Node.Id);
            return false;
        }
    }

    private static NodeExecutionResult MeteringFailed(string nodeId) => NodeExecutionResult.Fatal(
        $"AI step '{nodeId}' couldn't record its AI usage, so it was stopped (every AI call must be metered). " +
        "Check the host's usage storage (the engine_ai_usage table), then run it again.");

    private async Task<HashSet<string>> SensitiveNamesAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(context.SensitiveVariableNames ?? new HashSet<string>(), StringComparer.Ordinal);
        foreach (var variable in await _variables.GetForInstanceAsync(context.InstanceId, cancellationToken))
        {
            if (variable.IsSensitive) names.Add(variable.Name);
        }
        return names;
    }

    /// <summary>
    /// Replaces <c>{{ name }}</c> and <c>{{ variables.name }}</c> (any spacing, dotted paths under it too)
    /// for each sensitive name.
    /// </summary>
    internal static string Redact(string template, IReadOnlyCollection<string> sensitive)
    {
        foreach (var name in sensitive)
        {
            template = Regex.Replace(template, @"\{\{\s*(variables\.)?" + Regex.Escape(name) + @"(\.[^}\s]*)?\s*\}\}", Redacted);
        }
        return template;
    }

    private static JsonDocument ToOutput(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try { return JsonDocument.Parse(trimmed); } catch (JsonException) { /* not JSON after all */ }
        }
        return JsonDocument.Parse(JsonSerializer.Serialize(new { text }));
    }

    private static string? Text(JsonElement config, string name) =>
        config.ValueKind == JsonValueKind.Object && config.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    private static int? Int(JsonElement config, string name)
    {
        if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s) ? s : null;
    }
}
