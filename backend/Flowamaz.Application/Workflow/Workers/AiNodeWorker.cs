using System.Text.Json;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using NJsonSchema;

namespace Flowamaz.Application.Workflow.Workers;

/// <summary>
/// Executes AI nodes (NodeType.Ai). Resolves the model via the 5-level hierarchy, strips sensitive
/// variables before prompt construction, enforces per-node cost caps, supports BYOM providers,
/// walks a fallback chain on errors, validates output against an optional JSON schema with one
/// self-correction retry, and meters every call unconditionally.
/// </summary>
public sealed class AiNodeWorker : INodeWorker
{
    // Approximate cost per 1 000 tokens in USD — used only for the pre-call cap estimate.
    private static readonly Dictionary<string, (decimal Input, decimal Output)> TokenRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-4-5"] = (0.00025m, 0.00125m),
        ["claude-sonnet-4-6"] = (0.003m, 0.015m),
    };

    private const decimal DefaultInputCostPer1K = 0.003m;
    private const decimal DefaultOutputCostPer1K = 0.015m;

    private readonly IAiCompletionService _completionService;
    private readonly IModelResolutionService _modelResolution;
    private readonly IAiTokenMeteringService _metering;
    private readonly IVariableEvaluationService _variableEvaluation;
    private readonly ICredentialVaultService _credentialVault;
    private readonly IByomProviderService _byomProvider;
    private readonly ILogger<AiNodeWorker> _logger;

    public AiNodeWorker(
        IAiCompletionService completionService,
        IModelResolutionService modelResolution,
        IAiTokenMeteringService metering,
        IVariableEvaluationService variableEvaluation,
        ICredentialVaultService credentialVault,
        IByomProviderService byomProvider,
        ILogger<AiNodeWorker> logger)
    {
        _completionService = completionService;
        _modelResolution = modelResolution;
        _metering = metering;
        _variableEvaluation = variableEvaluation;
        _credentialVault = credentialVault;
        _byomProvider = byomProvider;
        _logger = logger;
    }

    public NodeType SupportedType => NodeType.Ai;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AiNodeWorker.ExecuteAsync enter instance={InstanceId} node={NodeId}",
            context.InstanceId, context.Node.Id);

        try
        {
            var config = context.Node.Config.RootElement;

            // ── Step 1: Model resolution ──────────────────────────────────────────
            var nodeModelOverride = GetString(config, "model");
            var fallbackModels = GetStringArray(config, "fallback_models");
            var maxTokens = GetInt(config, "max_tokens") ?? 1024;

            ModelConfig resolvedConfig;
            if (!string.IsNullOrWhiteSpace(nodeModelOverride))
            {
                // Node-level override: build a synthetic config using platform defaults as base.
                var baseConfig = await _modelResolution.ResolveModelConfigAsync(
                    AiFunctionIds.NodeExec, context.WorkspaceId, cancellationToken);
                resolvedConfig = baseConfig with { ModelId = nodeModelOverride };
            }
            else
            {
                resolvedConfig = await _modelResolution.ResolveModelConfigAsync(
                    AiFunctionIds.NodeExec, context.WorkspaceId, cancellationToken);
            }

            // ── Step 2: Sensitive variable stripping ──────────────────────────────
            var sensitiveNames = context.SensitiveVariableNames ?? (IReadOnlySet<string>)new HashSet<string>();
            var redactedVariables = BuildRedactedVariables(context.Variables, sensitiveNames);

            var strippedCount = sensitiveNames.Count(k => context.Variables.ContainsKey(k));
            if (strippedCount > 0)
            {
                _logger.LogWarning(
                    "AiNodeWorker: stripped {Count} sensitive variable(s) before AI call on node {NodeId}",
                    strippedCount, context.Node.Id);
            }

            // ── Step 3: Input construction ────────────────────────────────────────
            var promptTemplate = GetString(config, "prompt_template") ?? string.Empty;
            var systemPrompt = GetString(config, "system_prompt") ?? string.Empty;

            // Interpolate using variable evaluation service for instance-stored variables,
            // then do a second pass substituting redacted in-context variables.
            var interpolated = await _variableEvaluation.InterpolateAsync(
                context.InstanceId, promptTemplate, cancellationToken);
            var finalPrompt = SubstituteRedactedVariables(interpolated, redactedVariables);

            // ── Step 4: Cost cap check ────────────────────────────────────────────
            if (config.TryGetProperty("cost_cap_usd", out var capProp) &&
                capProp.TryGetDecimal(out var costCapUsd))
            {
                var estimated = EstimateCost(resolvedConfig.ModelId, finalPrompt, maxTokens);
                if (estimated > costCapUsd)
                {
                    _logger.LogWarning(
                        "AiNodeWorker: cost cap exceeded on node {NodeId}. Estimated={Estimated:F6} Cap={Cap:F6}",
                        context.Node.Id, estimated, costCapUsd);
                    return NodeExecutionResult.Fatal(
                        $"AI call on node '{context.Node.Id}' would exceed the per-node cost cap of ${costCapUsd:F4}. " +
                        $"Estimated cost: ${estimated:F4}. Reduce prompt size or increase the cost cap to proceed.");
                }
            }

            // ── Step 5 & 6: Provider routing + fallback chain ────────────────────
            string rawResponse;
            int tokensIn = 0, tokensOut = 0;
            Exception? lastException = null;

            var provider = GetString(config, "provider");
            var isByom = string.Equals(provider, AiProviders.Byom, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(resolvedConfig.Provider, AiProviders.Byom, StringComparison.OrdinalIgnoreCase);

            if (isByom)
            {
                var credentialIdStr = GetString(config, "byom_credential_id");
                if (!Guid.TryParse(credentialIdStr, out var byomCredentialId))
                {
                    return NodeExecutionResult.Fatal(
                        $"AI node '{context.Node.Id}' is configured as BYOM but 'byom_credential_id' is missing or invalid. " +
                        "Set byom_credential_id in the node config to the vault credential ID.");
                }

                try
                {
                    rawResponse = await _byomProvider.CompleteAsync(
                        context.WorkspaceId, byomCredentialId, resolvedConfig.ModelId,
                        finalPrompt, maxTokens, cancellationToken);
                    tokensIn = finalPrompt.Length / 4;
                    tokensOut = rawResponse.Length / 4;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "AiNodeWorker: BYOM completion failed on node {NodeId}", context.Node.Id);
                    MeterUsage(resolvedConfig, context, tokensIn, tokensOut);
                    return NodeExecutionResult.Retryable(
                        $"BYOM AI completion failed on node '{context.Node.Id}': {ex.Message}. " +
                        "Check the BYOM endpoint URL, API key, and model name in the credential.");
                }
            }
            else
            {
                (rawResponse, tokensIn, tokensOut, lastException) =
                    await RunWithFallbackAsync(resolvedConfig, systemPrompt, finalPrompt, fallbackModels, cancellationToken);

                if (lastException is not null)
                {
                    MeterUsage(resolvedConfig, context, tokensIn, tokensOut);
                    return NodeExecutionResult.Retryable(
                        $"AI completion failed after all fallbacks on node '{context.Node.Id}': {lastException.Message}. " +
                        "Check model availability and platform AI configuration.");
                }
            }

            // ── Step 7: Output schema validation ─────────────────────────────────
            if (config.TryGetProperty("output_schema", out var schemaProp))
            {
                var schemaJson = schemaProp.GetRawText();
                var (isValid, schemaErrors) = await ValidateAgainstSchemaAsync(rawResponse, schemaJson);

                if (!isValid)
                {
                    _logger.LogInformation(
                        "AiNodeWorker: output schema validation failed on node {NodeId}, attempting self-correction",
                        context.Node.Id);

                    var correctionPrompt =
                        $"Your previous response did not match the required JSON schema.\n\n" +
                        $"Schema:\n{schemaJson}\n\n" +
                        $"Validation errors:\n{schemaErrors}\n\n" +
                        "Please respond with valid JSON that matches the schema exactly.";

                    if (isByom)
                    {
                        var credentialIdStr = GetString(config, "byom_credential_id")!;
                        var byomCredentialId = Guid.Parse(credentialIdStr);
                        try
                        {
                            rawResponse = await _byomProvider.CompleteAsync(
                                context.WorkspaceId, byomCredentialId, resolvedConfig.ModelId,
                                correctionPrompt, maxTokens, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            MeterUsage(resolvedConfig, context, tokensIn, tokensOut);
                            return NodeExecutionResult.Fatal(
                                $"AI output on node '{context.Node.Id}' did not match schema after self-correction: {ex.Message}. " +
                                "Review the output_schema in the node config and ensure the model can produce valid JSON.");
                        }
                    }
                    else
                    {
                        (rawResponse, var corrIn, var corrOut, var corrEx) =
                            await RunWithFallbackAsync(resolvedConfig, systemPrompt, correctionPrompt, [], cancellationToken);
                        tokensIn += corrIn;
                        tokensOut += corrOut;

                        if (corrEx is not null)
                        {
                            MeterUsage(resolvedConfig, context, tokensIn, tokensOut);
                            return NodeExecutionResult.Fatal(
                                $"AI output on node '{context.Node.Id}' did not match schema after self-correction: {corrEx.Message}. " +
                                "Review the output_schema in the node config and ensure the model can produce valid JSON.");
                        }
                    }

                    var (secondValid, secondErrors) = await ValidateAgainstSchemaAsync(rawResponse, schemaJson);
                    if (!secondValid)
                    {
                        MeterUsage(resolvedConfig, context, tokensIn, tokensOut);
                        return NodeExecutionResult.Fatal(
                            $"AI output on node '{context.Node.Id}' did not match the required JSON schema after self-correction. " +
                            $"Errors: {secondErrors}. Review the output_schema or adjust the system prompt to enforce JSON output.");
                    }
                }
            }

            // ── Step 8: Metering ──────────────────────────────────────────────────
            MeterUsage(resolvedConfig, context, tokensIn, tokensOut);

            // ── Step 9: Return Ok ─────────────────────────────────────────────────
            JsonDocument output;
            try
            {
                output = JsonDocument.Parse(rawResponse);
            }
            catch (JsonException)
            {
                // Non-JSON response is valid — wrap in a JSON envelope.
                output = JsonDocument.Parse(JsonSerializer.Serialize(new { text = rawResponse }));
            }

            _logger.LogInformation(
                "AiNodeWorker.ExecuteAsync exit instance={InstanceId} node={NodeId}",
                context.InstanceId, context.Node.Id);

            return NodeExecutionResult.Ok(output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "AiNodeWorker.ExecuteAsync error instance={InstanceId} node={NodeId}",
                context.InstanceId, context.Node.Id);
            return NodeExecutionResult.Fatal(
                $"Unexpected error executing AI node '{context.Node.Id}': {ex.Message}. " +
                "Check the node configuration and platform AI settings.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private async Task<(string Response, int TokensIn, int TokensOut, Exception? Error)> RunWithFallbackAsync(
        ModelConfig baseConfig,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<string> fallbackModels,
        CancellationToken ct)
    {
        var modelsToTry = new List<ModelConfig> { baseConfig };
        foreach (var fb in fallbackModels)
            modelsToTry.Add(baseConfig with { ModelId = fb });

        Exception? lastEx = null;
        for (var i = 0; i < modelsToTry.Count; i++)
        {
            var attempt = modelsToTry[i];
            try
            {
                _logger.LogInformation(
                    "AiNodeWorker: attempting model={Model} (attempt {Attempt}/{Total})",
                    attempt.ModelId, i + 1, modelsToTry.Count);

                var result = await _completionService.CompleteAsync(attempt, systemPrompt, userPrompt, ct);
                var tokensIn = result.TokensInput;
                var tokensOut = result.TokensOutput;
                return (result.Text, tokensIn, tokensOut, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "AiNodeWorker: model={Model} failed (attempt {Attempt}/{Total}): {Error}",
                    attempt.ModelId, i + 1, modelsToTry.Count, ex.Message);
                lastEx = ex;
            }
        }

        return (string.Empty, 0, 0, lastEx!);
    }

    private static IReadOnlyDictionary<string, string> BuildRedactedVariables(
        IReadOnlyDictionary<string, JsonElement> variables,
        IReadOnlySet<string> sensitiveNames)
    {
        var result = new Dictionary<string, string>(variables.Count);
        foreach (var (key, value) in variables)
        {
            result[key] = sensitiveNames.Contains(key)
                ? "[SENSITIVE_REDACTED]"
                : value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
        }
        return result;
    }

    private static string SubstituteRedactedVariables(
        string template,
        IReadOnlyDictionary<string, string> variables)
    {
        var result = template;
        foreach (var (key, value) in variables)
        {
            result = result.Replace($"{{{{ {key} }}}}", value, StringComparison.Ordinal);
            result = result.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
        }
        return result;
    }

    private static decimal EstimateCost(string modelId, string prompt, int maxOutputTokens)
    {
        var (inputRate, outputRate) = TokenRates.TryGetValue(modelId, out var rates)
            ? rates
            : (DefaultInputCostPer1K, DefaultOutputCostPer1K);

        var inputTokens = prompt.Length / 4m;
        var estimatedInputCost = inputTokens / 1000m * inputRate;
        var estimatedOutputCost = maxOutputTokens / 1000m * outputRate;
        return estimatedInputCost + estimatedOutputCost;
    }

    private static async Task<(bool IsValid, string Errors)> ValidateAgainstSchemaAsync(
        string json, string schemaJson)
    {
        try
        {
            var schema = await JsonSchema.FromJsonAsync(schemaJson);
            var errors = schema.Validate(json);
            if (errors.Count == 0) return (true, string.Empty);

            var errorText = string.Join("; ", errors.Select(e => e.ToString()));
            return (false, errorText);
        }
        catch (Exception ex)
        {
            // Schema itself is malformed — treat as validation failure.
            return (false, $"Schema parse error: {ex.Message}");
        }
    }

    private void MeterUsage(ModelConfig config, NodeExecutionContext context, int tokensIn, int tokensOut)
    {
        var (inputRate, outputRate) = TokenRates.TryGetValue(config.ModelId, out var rates)
            ? rates
            : (DefaultInputCostPer1K, DefaultOutputCostPer1K);

        var cost = tokensIn / 1000m * inputRate + tokensOut / 1000m * outputRate;

        _metering.RecordUsage(
            AiFunctionIds.NodeExec,
            config.ModelId,
            config.Provider,
            null,
            context.WorkspaceId,
            tokensIn,
            tokensOut,
            cost);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var val)
        && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var val)
        && val.ValueKind == JsonValueKind.Number
        && val.TryGetInt32(out var i)
            ? i
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return [];
        if (!element.TryGetProperty(property, out var val)) return [];
        if (val.ValueKind != JsonValueKind.Array) return [];

        var result = new List<string>();
        foreach (var item in val.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (s is not null) result.Add(s);
            }
        }
        return result;
    }
}
