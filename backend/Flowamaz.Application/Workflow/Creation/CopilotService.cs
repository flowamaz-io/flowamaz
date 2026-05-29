using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

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
        "You are Flowamaz Co-pilot. The user is editing a workflow YAML. " +
        "Given the user's command and optionally the current YAML, produce ONLY a minimal YAML patch " +
        "(the changed/added nodes and edges). No explanation text. No markdown fences. Return valid YAML.";

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
}
