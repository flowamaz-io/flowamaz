using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowamaz.Core.Interfaces.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Ai;

/// <summary>
/// <see cref="IAiModelProvider"/> for Anthropic's Messages API with the host's own key (BYOK). Settings
/// (section <c>Ai:Anthropic</c>): <c>ApiKey</c> (required to run AI steps), <c>Model</c> (the default model
/// for steps that name none), <c>BaseUrl</c> (default https://api.anthropic.com), <c>TimeoutSeconds</c>
/// (default 120). Prices for the cost in metering: <c>Ai:Pricing:&lt;model&gt;:InputUsdPerMillionTokens</c>
/// and <c>OutputUsdPerMillionTokens</c>; without them the cost is recorded as 0 and the tokens are still exact.
/// The key is never logged.
/// </summary>
public sealed class AnthropicModelProvider : IAiModelProvider
{
    public const string HttpClientName = "flowamaz-ai-anthropic";
    public const string Section = "Ai:Anthropic";
    private const string ApiVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnthropicModelProvider> _logger;

    public AnthropicModelProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<AnthropicModelProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => "anthropic";

    public string? DefaultModel => Setting("Model");

    public async Task<AiCompletion> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = Setting("ApiKey");
        if (apiKey is null)
        {
            throw new AiProviderException(
                $"No Anthropic API key is configured. Set {Section}:ApiKey (for example the environment variable Ai__Anthropic__ApiKey).",
                retryable: false);
        }

        var baseUrl = (Setting("BaseUrl") ?? "https://api.anthropic.com").TrimEnd('/');
        var timeout = int.TryParse(Setting("TimeoutSeconds"), out var t) && t > 0 ? t : 120;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = JsonContent.Create(new MessagesRequest(
                request.Model,
                request.MaxTokens,
                string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt,
                [new Message("user", request.Prompt)])),
        };
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", ApiVersion);

        HttpResponseMessage response;
        try
        {
            response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(message, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException($"Anthropic didn't answer within {timeout}s.", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException($"Couldn't reach Anthropic: {ex.Message}", retryable: true, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode, body, request.Model);

            MessagesResponse? parsed;
            try { parsed = JsonSerializer.Deserialize<MessagesResponse>(body); }
            catch (JsonException ex) { throw new AiProviderException("Anthropic returned a reply that isn't valid JSON.", retryable: true, ex); }

            var text = string.Concat(parsed?.Content?.Where(c => c.Type == "text").Select(c => c.Text) ?? []);
            var model = string.IsNullOrWhiteSpace(parsed?.Model) ? request.Model : parsed!.Model!;
            var tokensIn = parsed?.Usage?.InputTokens ?? 0;
            var tokensOut = parsed?.Usage?.OutputTokens ?? 0;
            var cost = Cost(model, tokensIn, tokensOut) ?? Cost(request.Model, tokensIn, tokensOut);
            _logger.LogInformation(
                "AnthropicModelProvider completed model={Model} tokensIn={In} tokensOut={Out}", model, tokensIn, tokensOut);
            return new AiCompletion(text, model, tokensIn, tokensOut, cost);
        }
    }

    private static AiProviderException Failure(HttpStatusCode status, string body, string model)
    {
        var detail = ErrorMessage(body);
        var code = (int)status;
        return code switch
        {
            401 or 403 => new AiProviderException(
                $"Anthropic rejected the API key (HTTP {code}): {detail} Check {Section}:ApiKey.", retryable: false),
            404 => new AiProviderException(
                $"Anthropic doesn't know the model '{model}' (HTTP 404): {detail} Check the step's `model` or {Section}:Model.", retryable: false),
            408 or 409 or 429 or >= 500 => new AiProviderException(
                $"Anthropic is busy or unavailable (HTTP {code}): {detail}", retryable: true),
            _ => new AiProviderException($"Anthropic refused the request (HTTP {code}): {detail}", retryable: false),
        };
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var m))
                return m.GetString() ?? string.Empty;
        }
        catch (JsonException) { }
        return body.Length > 300 ? body[..300] : body;
    }

    private decimal? Cost(string model, int tokensIn, int tokensOut)
    {
        var section = _configuration.GetSection($"Ai:Pricing:{model}");
        if (!decimal.TryParse(section["InputUsdPerMillionTokens"], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var inPrice)
            || !decimal.TryParse(section["OutputUsdPerMillionTokens"], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var outPrice))
        {
            return null;
        }
        return (tokensIn * inPrice + tokensOut * outPrice) / 1_000_000m;
    }

    private string? Setting(string key)
    {
        var value = _configuration[$"{Section}:{key}"];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record MessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? System,
        [property: JsonPropertyName("messages")] Message[] Messages);

    private sealed record Message(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class MessagesResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
        [JsonPropertyName("usage")] public UsageBlock? Usage { get; set; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class UsageBlock
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    }
}
