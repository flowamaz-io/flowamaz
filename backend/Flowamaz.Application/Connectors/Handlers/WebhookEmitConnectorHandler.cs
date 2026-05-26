using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Emits a webhook to an external URL with an optional HMAC-SHA256 signature.
/// Input: { url: string, payload: object, secret?: string }
/// Returns: { status_code: int, success: bool }
/// </summary>
public sealed class WebhookEmitHandler : IConnectorOperationHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookEmitHandler> _logger;

    public WebhookEmitHandler(IHttpClientFactory httpClientFactory, ILogger<WebhookEmitHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "webhook-emit";
    public string OperationId => "send-webhook";

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[WebhookEmit:send-webhook] ExecuteAsync entry");

        var root = input.RootElement;
        var url = root.GetProperty("url").GetString() ?? throw new InvalidOperationException("'url' is required.");
        var payloadEl = root.GetProperty("payload");
        var payloadJson = payloadEl.GetRawText();

        var secret = root.TryGetProperty("secret", out var secretEl) ? secretEl.GetString() : null;

        using var client = _httpClientFactory.CreateClient("webhook-emit-connector");
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(secret))
        {
            var sig = ComputeHmacSha256(payloadJson, secret);
            request.Headers.Add("X-Flowamaz-Signature", $"sha256={sig}");
        }

        using var response = await client.SendAsync(request, ct);
        var statusCode = (int)response.StatusCode;
        var success = response.IsSuccessStatusCode;

        _logger.LogInformation("[WebhookEmit:send-webhook] ExecuteAsync exit status={Status}", statusCode);
        return JsonDocument.Parse($"{{\"status_code\":{statusCode},\"success\":{success.ToString().ToLowerInvariant()}}}");
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
