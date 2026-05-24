using System.Text.Json;
using Flowamaz.Application.Ai;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Ai;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Resolves the model config for a function in a workspace context by walking
/// FUNCTIONAL.md §5.3 hierarchy: workspace → org → platform. Node and workflow levels are
/// applied by the F4 worker before calling this service (it accepts whichever wins).
/// Capability validation runs on every resolve so config violations surface up front.
/// </summary>
public sealed class ModelResolutionService : IModelResolutionService
{
    private readonly FlowAmazDbContext _db;
    private readonly AiOptions _aiOptions;
    private readonly ILogger<ModelResolutionService> _logger;

    public ModelResolutionService(
        FlowAmazDbContext db,
        IOptions<AiOptions> aiOptions,
        ILogger<ModelResolutionService> logger)
    {
        _db = db;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    public async Task<ModelConfig> ResolveModelConfigAsync(
        string functionId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(functionId);
        _logger.LogDebug(
            "ModelResolutionService.ResolveModelConfigAsync enter func={FunctionId} workspace={WorkspaceId}",
            functionId, workspaceId);

        try
        {
            var resolved =
                await ResolveFromWorkspaceAsync(functionId, workspaceId, cancellationToken)
                ?? await ResolveFromOrgAsync(functionId, workspaceId, cancellationToken)
                ?? await ResolveFromPlatformAsync(functionId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"No platform AI config found for function '{functionId}' — seed data is incomplete.");

            var model = await _db.ModelCatalogue
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ModelId == resolved.ModelId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Resolved model '{resolved.ModelId}' not in model_catalogue — catalogue out of sync with config.");

            ModelCapabilityValidator.Validate(functionId, model);

            var apiKey = resolved.KeySource == AiKeySource.Platform
                ? ResolvePlatformKey(model.Provider)
                : null; // BYOK key is fetched by caller from credential vault by provider id

            var config = new ModelConfig(
                ModelId: model.ModelId,
                Provider: model.Provider,
                ApiKeySource: resolved.KeySource,
                ApiKey: apiKey,
                HasVision: model.HasVision,
                MaxContextTokens: model.MaxContextTokens,
                SupportsJsonMode: model.SupportsJsonMode,
                SupportsStreaming: model.SupportsStreaming);

            _logger.LogDebug(
                "ModelResolutionService.ResolveModelConfigAsync exit func={FunctionId} model={ModelId} provider={Provider} keySource={KeySource}",
                functionId, config.ModelId, config.Provider, config.ApiKeySource);

            return config;
        }
        catch (ConfigViolationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ModelResolutionService.ResolveModelConfigAsync error func={FunctionId} workspace={WorkspaceId}",
                functionId, workspaceId);
            throw;
        }
    }

    private async Task<ResolvedOverride?> ResolveFromWorkspaceAsync(
        string functionId, Guid workspaceId, CancellationToken ct)
    {
        var cfg = await _db.WorkspaceAiConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId, ct);
        return cfg is null ? null : ParseOverride(cfg.FunctionOverridesJson, functionId);
    }

    private async Task<ResolvedOverride?> ResolveFromOrgAsync(
        string functionId, Guid workspaceId, CancellationToken ct)
    {
        // Org lookup requires a workspace→org link which arrives in prompt 02.
        // Until then this layer always returns null and we fall through to platform.
        await Task.CompletedTask;
        _ = functionId; _ = workspaceId; _ = ct;
        return null;
    }

    private async Task<ResolvedOverride?> ResolveFromPlatformAsync(
        string functionId, CancellationToken ct)
    {
        var cfg = await _db.PlatformAiConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.FunctionId == functionId && c.IsEnabled, ct);
        if (cfg is null) return null;
        return new ResolvedOverride(cfg.ModelId, cfg.Provider, cfg.KeySource);
    }

    private string? ResolvePlatformKey(string provider) => provider switch
    {
        AiProviders.Anthropic => Empty(_aiOptions.AnthropicPlatformKey),
        AiProviders.Google    => Empty(_aiOptions.GooglePlatformKey),
        _ => null,
    };

    private static string? Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static ResolvedOverride? ParseOverride(string json, string functionId)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(functionId, out var fn)) return null;

            var modelId = fn.GetProperty("modelId").GetString()
                ?? throw new ConfigViolationException(
                    $"AI config override for function '{functionId}' is missing 'modelId'.");
            var provider = fn.GetProperty("provider").GetString()
                ?? throw new ConfigViolationException(
                    $"AI config override for function '{functionId}' is missing 'provider'.");
            var keySourceStr = fn.TryGetProperty("keySource", out var ks)
                ? ks.GetString() ?? nameof(AiKeySource.Platform)
                : nameof(AiKeySource.Platform);
            var keySource = Enum.Parse<AiKeySource>(keySourceStr, ignoreCase: true);
            return new ResolvedOverride(modelId, provider, keySource);
        }
        catch (ConfigViolationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            // Malformed override JSON is a config-time violation, not a runtime crash —
            // surface it through the same path as capability gate failures so callers can react uniformly.
            throw new ConfigViolationException(
                $"AI config override for function '{functionId}' is malformed: {ex.Message}");
        }
    }

    private sealed record ResolvedOverride(string ModelId, string Provider, AiKeySource KeySource);
}
