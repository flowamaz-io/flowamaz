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
    /// <param name="maxTokens">Output token cap (FIX 6: sized by caller to avoid paying for unused capacity).</param>
    Task<AiCompletionResult> CompleteAsync(
        ModelConfig config, string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default, int maxTokens = 1024);

    /// <summary>
    /// Vision completion (F3). Sends an image alongside a text prompt — requires HasVision=true model.
    /// </summary>
    Task<AiCompletionResult> CompleteWithImageAsync(
        ModelConfig config, string systemPrompt, string imageBase64, string mimeType,
        string additionalText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a single request to the Anthropic Batch API (POST /v1/messages/batches).
    /// Returns the Anthropic batch ID (e.g. "msgbatch_01..."). The caller stores this ID and
    /// polls via <see cref="PollBatchResultAsync"/> at a later time for the 50% cost saving.
    /// </summary>
    Task<string> SubmitBatchAsync(
        ModelConfig config, string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls a previously submitted batch. Returns null if the batch is still processing.
    /// Returns the completion result when the batch status is "ended" and succeeded.
    /// </summary>
    Task<AiCompletionResult?> PollBatchResultAsync(
        string batchId, ModelConfig config, CancellationToken cancellationToken = default);
}
