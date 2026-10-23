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
            "Ask a workspace admin to add an AI provider key in Settings → AI, or contact support@flowamaz.io " +
            "if you are on a managed plan.",
            httpStatusCode: 503)
    {
    }
}
