using System.Net.Http.Json;
using System.Text.Json;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// <see cref="IAiCompletionService"/> backed by the Anthropic Messages API. When the resolved
/// <see cref="ModelConfig.ApiKey"/> is present the real provider call is made over an injected
/// <see cref="HttpClient"/> (token counts come from the provider's usage block); when no key is
/// configured (dev/test) it falls back to a deterministic local completion and logs a warning so
/// the request path stays key-free. Metering is the caller's responsibility — this only returns the
/// text and token counts. The HTTP seam keeps the provider call unit-testable without a network.
/// </summary>
public sealed class AiCompletionService : IAiCompletionService
{
    /// <summary>Named <see cref="HttpClient"/> for the Anthropic provider.</summary>
    public const string HttpClientName = "anthropic";

    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiOptions _options;
    private readonly ILogger<AiCompletionService> _logger;

    public AiCompletionService(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options, ILogger<AiCompletionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiCompletionResult> CompleteAsync(
        ModelConfig config, string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AiCompletionService.CompleteAsync enter model={ModelId} provider={Provider}", config.ModelId, config.Provider);

        if (_options.UseStubCompletion || string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogWarning(
                "AiCompletionService: returning local stub completion for {Provider}/{Model} (stubMode={Stub}, hasKey={HasKey})",
                config.Provider, config.ModelId, _options.UseStubCompletion, !string.IsNullOrEmpty(config.ApiKey));
            return StubResult(systemPrompt, userPrompt);
        }

        try
        {
            var result = await CallAnthropicAsync(config, systemPrompt, userPrompt, cancellationToken);
            _logger.LogDebug(
                "AiCompletionService.CompleteAsync exit tokensIn={In} tokensOut={Out}", result.TokensInput, result.TokensOutput);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AiCompletionService.CompleteAsync error model={ModelId} provider={Provider}", config.ModelId, config.Provider);
            throw;
        }
    }

    private async Task<AiCompletionResult> CallAnthropicAsync(
        ModelConfig config, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        request.Content = JsonContent.Create(new
        {
            model = config.ModelId,
            max_tokens = MaxTokens,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        });

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseResponse(doc.RootElement);
    }

    private static AiCompletionResult ParseResponse(JsonElement root)
    {
        var text = string.Empty;
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var textBlock))
                {
                    text = textBlock.GetString() ?? string.Empty;
                    break;
                }
            }
        }

        var tokensInput = 0;
        var tokensOutput = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var input)) tokensInput = input.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var output)) tokensOutput = output.GetInt32();
        }

        return new AiCompletionResult(text, tokensInput, tokensOutput);
    }

    // Deterministic local completion: lead sentence + the supplied facts so instance-specific values
    // (amounts, names) always appear. Token counts are estimated (≈4 chars/token) for realistic metering.
    private static AiCompletionResult StubResult(string systemPrompt, string userPrompt)
    {
        var text = $"Status summary:\n{userPrompt.Trim()}";
        return new AiCompletionResult(text, EstimateTokens(systemPrompt) + EstimateTokens(userPrompt), EstimateTokens(text));
    }

    private static int EstimateTokens(string text) => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);
}
