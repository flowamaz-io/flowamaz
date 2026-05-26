using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Single-shot text completion against a resolved <see cref="ModelConfig"/>. The provider-agnostic
/// seam every AI feature calls; the caller resolves the model via IModelResolutionService and meters
/// the returned token counts. (Phase 2 ships a deterministic local implementation; the real provider
/// SDK call is wired when a platform key is configured.)
/// </summary>
public interface IAiCompletionService
{
    Task<AiCompletionResult> CompleteAsync(
        ModelConfig config, string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vision completion (F3). Sends an image alongside a text prompt — requires HasVision=true model.
    /// </summary>
    Task<AiCompletionResult> CompleteWithImageAsync(
        ModelConfig config, string systemPrompt, string imageBase64, string mimeType,
        string additionalText, CancellationToken cancellationToken = default);
}
