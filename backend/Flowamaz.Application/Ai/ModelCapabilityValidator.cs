using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Ai;
using Flowamaz.Core.Exceptions;

namespace Flowamaz.Application.Ai;

/// <summary>
/// Enforces FUNCTIONAL.md §5.4 capability requirements per AI function. Fires at config-save time
/// (called from <c>IModelResolutionService.ResolveModelConfigAsync</c>) so config violations
/// surface up front, never at AI-call time.
/// </summary>
public static class ModelCapabilityValidator
{
    public const int MinDocParseContextTokens = 100_000;

    /// <summary>
    /// Throws <see cref="ConfigViolationException"/> when the model's capabilities do not
    /// satisfy the requirements of the given function. Returns silently on success.
    /// </summary>
    public static void Validate(string functionId, ModelCatalogue model)
    {
        ArgumentException.ThrowIfNullOrEmpty(functionId);
        ArgumentNullException.ThrowIfNull(model);

        switch (functionId)
        {
            case AiFunctionIds.VisualInput when !model.HasVision:
                throw new ConfigViolationException(
                    "AI_CAPABILITY_MISMATCH",
                    $"Function '{functionId}' requires a model with vision capability, but " +
                    $"'{model.ModelId}' (provider {model.Provider}) does not support vision. " +
                    $"Pick a vision-capable model — e.g. claude-sonnet-4-6, gpt-4o, gemini-2-0-flash.");

            case AiFunctionIds.DocParse when model.MaxContextTokens < MinDocParseContextTokens:
                throw new ConfigViolationException(
                    "AI_CAPABILITY_MISMATCH",
                    $"Function '{functionId}' requires a model with at least " +
                    $"{MinDocParseContextTokens:N0} context tokens, but '{model.ModelId}' " +
                    $"supports only {model.MaxContextTokens:N0}. " +
                    $"Pick a long-context model — e.g. claude-sonnet-4-6 (200k), gemini-1-5-pro (1M).");
        }

        if (!model.IsEnabled)
        {
            throw new ConfigViolationException(
                "MODEL_DISABLED",
                $"Model '{model.ModelId}' is disabled in the model catalogue. " +
                $"Enable it in platform admin or pick a different model for '{functionId}'.");
        }
    }
}
