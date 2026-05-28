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
    private const string BatchesUrl = "https://api.anthropic.com/v1/messages/batches";
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultMaxTokens = 1024;

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
        ModelConfig config, string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default, int maxTokens = DefaultMaxTokens)
    {
        _logger.LogDebug("AiCompletionService.CompleteAsync enter model={ModelId} provider={Provider} maxTokens={MaxTokens}",
            config.ModelId, config.Provider, maxTokens);

        if (_options.UseStubCompletion || string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogWarning(
                "AiCompletionService: returning local stub completion for {Provider}/{Model} (stubMode={Stub}, hasKey={HasKey})",
                config.Provider, config.ModelId, _options.UseStubCompletion, !string.IsNullOrEmpty(config.ApiKey));
            return StubResult(systemPrompt, userPrompt);
        }

        try
        {
            var result = await CallAnthropicAsync(config, systemPrompt, userPrompt, maxTokens, cancellationToken);
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
        ModelConfig config, string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        request.Content = JsonContent.Create(new
        {
            model = config.ModelId,
            max_tokens = maxTokens,
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

    public async Task<AiCompletionResult> CompleteWithImageAsync(
        ModelConfig config, string systemPrompt, string imageBase64, string mimeType,
        string additionalText, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AiCompletionService.CompleteWithImageAsync enter model={ModelId}", config.ModelId);

        if (_options.UseStubCompletion || string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogWarning("AiCompletionService.CompleteWithImageAsync: stub mode");
            const string stubJson = """{"nodes":[{"id":"n1","label":"Start","type":"trigger","x":100,"y":100,"confidence":0.99,"annotations":[]},{"id":"n2","label":"Process","type":"action","x":100,"y":200,"confidence":0.95,"annotations":[]},{"id":"n3","label":"End","type":"end","x":100,"y":300,"confidence":0.99,"annotations":[]}],"edges":[{"from":"n1","to":"n2","label":null},{"from":"n2","to":"n3","label":null}],"low_confidence":[]}""";
            return new AiCompletionResult(stubJson, EstimateTokens(imageBase64 + additionalText), EstimateTokens(stubJson));
        }

        try
        {
            var result = await CallAnthropicWithImageAsync(config, systemPrompt, imageBase64, mimeType, additionalText, cancellationToken);
            _logger.LogDebug("AiCompletionService.CompleteWithImageAsync exit tokensIn={In} tokensOut={Out}", result.TokensInput, result.TokensOutput);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AiCompletionService.CompleteWithImageAsync error model={ModelId}", config.ModelId);
            throw;
        }
    }

    private async Task<AiCompletionResult> CallAnthropicWithImageAsync(
        ModelConfig config, string systemPrompt, string imageBase64, string mimeType,
        string additionalText, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        request.Content = JsonContent.Create(new
        {
            model = config.ModelId,
            max_tokens = DefaultMaxTokens,
            system = systemPrompt,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = mimeType, data = imageBase64 } },
                        new { type = "text", text = additionalText },
                    },
                },
            },
        });

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseResponse(doc.RootElement);
    }

    public async Task<string> SubmitBatchAsync(
        ModelConfig config, string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AiCompletionService.SubmitBatchAsync enter model={ModelId}", config.ModelId);

        if (_options.UseStubCompletion || string.IsNullOrEmpty(config.ApiKey))
        {
            var stubId = $"stub-batch-{Guid.NewGuid():N}";
            _logger.LogWarning("AiCompletionService.SubmitBatchAsync: stub mode → {BatchId}", stubId);
            return stubId;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, BatchesUrl);
            request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            request.Content = JsonContent.Create(new
            {
                requests = new[]
                {
                    new
                    {
                        custom_id = "insight",
                        @params = new
                        {
                            model = config.ModelId,
                            max_tokens = DefaultMaxTokens,
                            system = systemPrompt,
                            messages = new[] { new { role = "user", content = userPrompt } },
                        },
                    },
                },
            });

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var batchId = doc.RootElement.GetProperty("id").GetString()!;
            _logger.LogInformation("AiCompletionService.SubmitBatchAsync exit batchId={BatchId}", batchId);
            return batchId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AiCompletionService.SubmitBatchAsync error model={ModelId}", config.ModelId);
            throw;
        }
    }

    public async Task<AiCompletionResult?> PollBatchResultAsync(
        string batchId, ModelConfig config, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AiCompletionService.PollBatchResultAsync enter batchId={BatchId}", batchId);

        if (_options.UseStubCompletion || string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogDebug("AiCompletionService.PollBatchResultAsync: stub mode — returning immediate result");
            return StubResult("batch-system", batchId);
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            // Check processing status
            using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"{BatchesUrl}/{batchId}");
            statusRequest.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
            statusRequest.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            using var statusResponse = await client.SendAsync(statusRequest, cancellationToken);
            statusResponse.EnsureSuccessStatusCode();

            await using var statusStream = await statusResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var statusDoc = await JsonDocument.ParseAsync(statusStream, cancellationToken: cancellationToken);
            var status = statusDoc.RootElement.GetProperty("processing_status").GetString();
            if (status != "ended")
            {
                _logger.LogDebug("AiCompletionService.PollBatchResultAsync batchId={BatchId} still {Status}", batchId, status);
                return null;
            }

            // Retrieve JSONL results
            using var resultsRequest = new HttpRequestMessage(HttpMethod.Get, $"{BatchesUrl}/{batchId}/results");
            resultsRequest.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
            resultsRequest.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            using var resultsResponse = await client.SendAsync(resultsRequest, cancellationToken);
            resultsResponse.EnsureSuccessStatusCode();

            var jsonl = await resultsResponse.Content.ReadAsStringAsync(cancellationToken);
            var firstLine = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstLine is null) return null;

            using var lineDoc = JsonDocument.Parse(firstLine);
            var result = lineDoc.RootElement.GetProperty("result");
            if (result.GetProperty("type").GetString() != "succeeded") return null;

            var parsed = ParseResponse(result.GetProperty("message"));
            _logger.LogInformation("AiCompletionService.PollBatchResultAsync exit batchId={BatchId} tokensIn={In} tokensOut={Out}",
                batchId, parsed.TokensInput, parsed.TokensOutput);
            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AiCompletionService.PollBatchResultAsync error batchId={BatchId}", batchId);
            throw;
        }
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
