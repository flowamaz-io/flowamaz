using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Models;

/// <summary>
/// Result of <c>IModelResolutionService.ResolveModelConfigAsync</c>. Carries the model,
/// provider, key source, and resolved API key for one AI call. The API key may be empty
/// for BYOK paths where the caller will look up the credential separately.
/// </summary>
public sealed record ModelConfig(
    string ModelId,
    string Provider,
    AiKeySource ApiKeySource,
    string? ApiKey,
    /// <summary>Capability flags resolved from model_catalogue — for downstream sanity checks.</summary>
    bool HasVision,
    int MaxContextTokens,
    bool SupportsJsonMode,
    bool SupportsStreaming);
