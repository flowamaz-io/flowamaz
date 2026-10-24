namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when an AI function is called but no provider key resolves for it and the local stub is
/// not explicitly allowed (<c>Ai:AllowStubWithoutKey</c> / <c>Ai:UseStubCompletion</c>). Returning
/// canned output in that case would show users fabricated AI results, so we fail loudly instead.
/// Maps to HTTP 503. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class AiNotConfiguredException : AppException
{
    public AiNotConfiguredException(string provider, string modelId)
        : base(
            "AI_NOT_CONFIGURED",
            $"AI features are not configured for this workspace ({provider}/{modelId} has no API key). " +
            "Ask a workspace admin to add an AI provider key in Settings → Workspace → AI model configuration, " +
            "or contact support@flowamaz.io if you are on a managed plan.",
            httpStatusCode: 503)
    {
    }

    private AiNotConfiguredException(string message) : base("AI_NOT_CONFIGURED", message, httpStatusCode: 503) { }

    /// <summary>
    /// Workflow generation (plain English, photo, conversation, document) must never run on canned stub
    /// output: it would show fabricated steps as if the AI had read the user's input. Throws when the
    /// resolved model has no key (local stub mode) so the user is told what is actually missing.
    /// </summary>
    public static void ThrowIfNoKey(Models.ModelConfig config, string feature)
    {
        if (!string.IsNullOrWhiteSpace(config.ApiKey)) return;
        throw new AiNotConfiguredException(
            $"{feature} needs an AI provider key, and none is configured ({config.Provider}/{config.ModelId}). " +
            "Set Ai__AnthropicPlatformKey on the server, or add your own key in Settings → Workspace → AI model configuration. " +
            "Until then, start from a template or use Draw on canvas.");
    }
}
