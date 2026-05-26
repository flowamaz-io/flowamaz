using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Phase 2 implementation of <see cref="IAiCompletionService"/>. The provider SDK call is not yet
/// wired (no platform key in dev), so this produces a deterministic local completion from the
/// prompt — enough to power the CEO narrative and keep the request path key-free. Token counts are
/// estimated (≈4 chars/token) so metering still records realistic usage. When a real provider key
/// is configured, the live call replaces this body.
/// </summary>
public sealed class AiCompletionService : IAiCompletionService
{
    private readonly ILogger<AiCompletionService> _logger;

    public AiCompletionService(ILogger<AiCompletionService> logger) => _logger = logger;

    public Task<AiCompletionResult> CompleteAsync(
        ModelConfig config, string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AiCompletionService.CompleteAsync enter model={ModelId} provider={Provider}", config.ModelId, config.Provider);

        // Deterministic summary: lead sentence + the supplied facts so instance-specific values
        // (amounts, names) always appear in the output.
        var text = $"Status summary:\n{userPrompt.Trim()}";
        var result = new AiCompletionResult(text, EstimateTokens(systemPrompt) + EstimateTokens(userPrompt), EstimateTokens(text));

        _logger.LogDebug(
            "AiCompletionService.CompleteAsync exit tokensIn={In} tokensOut={Out}", result.TokensInput, result.TokensOutput);
        return Task.FromResult(result);
    }

    private static int EstimateTokens(string text) => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);
}
