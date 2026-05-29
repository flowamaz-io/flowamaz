using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Flowamaz.Application.Workflow.Creation;

public sealed class CopilotService : ICopilotService
{
    private readonly IRateLimitService _rateLimit;
    private readonly IAiBudgetService _budget;
    private readonly ICopilotPatternMatcher _matcher;
    private readonly ISemanticCacheService _cache;
    private readonly IModelResolutionService _modelResolution;
    private readonly IAiCompletionService _ai;
    private readonly IAiTokenMeteringService _metering;
    private readonly ILogger<CopilotService> _log;

    private const string FunctionId = AiFunctionIds.Copilot;
    private const string SystemPrompt =
        "You are a workflow YAML editor for Flowamaz workflows.\n" +
        "The user will give you a command to modify the workflow.\n" +
        "You must respond with ONLY a valid YAML patch — nothing else.\n" +
        "No explanation, no markdown, no code blocks, no JSONPath expressions.\n\n" +
        "The patch MUST follow this exact format:\n\n" +
        "To ADD a new node:\n" +
        "spec:\n" +
        "  nodes:\n" +
        "    - id: new-node-id\n" +
        "      type: action\n" +
        "      label: \"Node Label\"\n" +
        "      config: {}\n\n" +
        "To MODIFY an existing node (include ALL fields, use id to match):\n" +
        "spec:\n" +
        "  nodes:\n" +
        "    - id: existing-node-id\n" +
        "      type: human-gate\n" +
        "      label: \"Existing Label\"\n" +
        "      config: {}\n" +
        "      timeout:\n" +
        "        seconds: 7200\n\n" +
        "To ADD a new edge:\n" +
        "spec:\n" +
        "  edges:\n" +
        "    - id: e99\n" +
        "      from: source-node-id\n" +
        "      to: target-node-id\n\n" +
        "To MODIFY an existing edge:\n" +
        "spec:\n" +
        "  edges:\n" +
        "    - id: existing-edge-id\n" +
        "      from: source-node-id\n" +
        "      to: new-target-id\n\n" +
        "You may include both nodes and edges in one patch.\n" +
        "NEVER use JSONPath, dot notation, or bracket notation like nodes[id=x].field.\n" +
        "ALWAYS return complete node objects when modifying existing nodes.\n" +
        "ALWAYS use the node id to identify which node to update.\n" +
        "If you cannot determine the correct patch, respond with:\n" +
        "spec:\n" +
        "  nodes: []\n" +
        "  edges: []";

    public CopilotService(
        IRateLimitService rateLimit,
        IAiBudgetService budget,
        ICopilotPatternMatcher matcher,
        ISemanticCacheService cache,
        IModelResolutionService modelResolution,
        IAiCompletionService ai,
        IAiTokenMeteringService metering,
        ILogger<CopilotService> log)
    {
        _rateLimit = rateLimit;
        _budget = budget;
        _matcher = matcher;
        _cache = cache;
        _modelResolution = modelResolution;
        _ai = ai;
        _metering = metering;
        _log = log;
    }

    public async Task<CopilotResult> ProcessCommandAsync(
        string command, string? yamlContent, Guid workspaceId, string userId,
        CancellationToken cancellationToken = default)
    {
        _log.LogInformation("CopilotService.ProcessCommandAsync entry workspaceId={WorkspaceId} userId={UserId}", workspaceId, userId);

        // 1. Rate check
        var rateLimitKey = $"copilot:{workspaceId}:{userId}";
        var allowed = await _rateLimit.CheckAndIncrementAsync(rateLimitKey, 60, 3600, cancellationToken);
        if (!allowed)
        {
            _log.LogWarning("CopilotService.ProcessCommandAsync rate_limited userId={UserId}", userId);
            return new CopilotResult(false, null, null, false, null,
                "Rate limit exceeded. You can send 60 Co-pilot commands per hour. Wait a moment and try again.");
        }

        // 2. Budget check
        var hasBudget = await _budget.IsBudgetAvailableAsync(workspaceId, cancellationToken);
        if (!hasBudget)
        {
            _log.LogWarning("CopilotService.ProcessCommandAsync budget_exceeded workspaceId={WorkspaceId}", workspaceId);
            return new CopilotResult(false, null, null, false, null,
                "AI token budget exhausted for this workspace. Upgrade your plan or increase the budget to continue using Co-pilot.");
        }

        // 3. Pattern match — zero AI cost on hit
        var patternResult = _matcher.TryMatch(command, yamlContent);
        if (patternResult is not null)
        {
            _log.LogInformation("CopilotService.ProcessCommandAsync exit pattern_hit={Pattern}", patternResult.PatternName);
            return new CopilotResult(true, patternResult.YamlPatch, patternResult.PatternName, false, 0, null);
        }

        // 4. Semantic cache
        var cacheInput = BuildCacheInput(command, yamlContent);
        var cacheKey = _cache.ComputeKey(FunctionId, workspaceId, cacheInput);
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _log.LogInformation("CopilotService.ProcessCommandAsync exit cache_hit");
            return new CopilotResult(true, cached, null, true, 0, null);
        }

        // 5. AI call
        var config = await _modelResolution.ResolveModelConfigAsync(FunctionId, workspaceId, cancellationToken);
        var userPrompt = BuildUserPrompt(command, yamlContent);
        var aiResult = await _ai.CompleteAsync(config, SystemPrompt, userPrompt, cancellationToken);

        // 5a. Retry once if the AI returned an invalid patch format
        if (!IsValidPatch(aiResult.Text))
        {
            _log.LogWarning("CopilotService.ProcessCommandAsync invalid_patch_format — retrying with explicit reminder");
            var retryPrompt = userPrompt +
                "\n\nIMPORTANT: Return ONLY valid YAML starting with 'spec:'. " +
                "Do NOT use JSONPath or dot notation. " +
                "Return the complete node object with all fields.";
            aiResult = await _ai.CompleteAsync(config, SystemPrompt, retryPrompt, cancellationToken);
        }

        // 6. Cache write (fire-and-forget)
        _ = _cache.SetAsync(cacheKey, aiResult.Text, TimeSpan.FromHours(24), CancellationToken.None);

        // 7. Metering (fire-and-forget)
        var costUsd = (aiResult.TokensInput + aiResult.TokensOutput) * 0.000000003m;
        _metering.RecordUsage(FunctionId, config.ModelId, config.Provider, null, workspaceId,
            aiResult.TokensInput, aiResult.TokensOutput, costUsd);

        _log.LogInformation("CopilotService.ProcessCommandAsync exit ai_tokens={Tokens}", aiResult.TokensInput + aiResult.TokensOutput);
        return new CopilotResult(true, aiResult.Text, null, false, aiResult.TokensInput + aiResult.TokensOutput, null);
    }

    private static string BuildCacheInput(string command, string? yamlContent)
    {
        var sb = new StringBuilder(command);
        if (!string.IsNullOrWhiteSpace(yamlContent))
        {
            // Include a hash of the yaml so different graphs yield different cache keys
            var yamlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yamlContent)))[..16];
            sb.Append('|').Append(yamlHash);
        }
        return sb.ToString();
    }

    private static string BuildUserPrompt(string command, string? yamlContent) =>
        string.IsNullOrWhiteSpace(yamlContent)
            ? $"Command: {command}"
            : $"Command: {command}\n\nCurrent YAML:\n{yamlContent}";

    private static bool IsValidPatch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var doc = deserializer.Deserialize<Dictionary<string, object>>(text);
            if (doc is null || !doc.ContainsKey("spec")) return false;
            if (doc["spec"] is not Dictionary<object, object> spec) return false;
            return spec.ContainsKey("nodes") || spec.ContainsKey("edges");
        }
        catch
        {
            return false;
        }
    }
}
