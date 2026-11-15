using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowamaz.Application.Connectors.Security;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// POSTs a JSON payload to a URL. When an HmacSecret credential is set, the body is signed:
/// <c>X-Flowamaz-Signature: sha256=&lt;hex&gt;</c> plus <c>X-Flowamaz-Timestamp</c> (signature covers
/// "timestamp.body" so a captured request cannot be replayed later). Input: { url, payload }.
/// </summary>
public sealed class WebhookEmitHandler : IConnectorOperationHandler
{
    public const string HttpClientName = "webhook-emit-connector";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookEmitHandler> _logger;

    public WebhookEmitHandler(IHttpClientFactory httpClientFactory, ILogger<WebhookEmitHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "webhook-emit";
    public string OperationId => "send-webhook";

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[WebhookEmit:send-webhook] ExecuteAsync entry");
        const string op = "webhook-emit/send-webhook";
        var root = input.RootElement;
        ConnectorInput.RejectSecretInput(root, op, "secret");
        var url = EgressGuard.RequireHttpUrl(ConnectorInput.Optional(root, "url"));
        if (!root.TryGetProperty("payload", out var payloadEl))
            throw new Flowamaz.Core.Exceptions.ConnectorOperationException($"{op}: input 'payload' is required.");
        var payloadJson = payloadEl.ValueKind == JsonValueKind.String ? payloadEl.GetString() ?? "null" : payloadEl.GetRawText();

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        if (credential is not null)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Headers.Add("X-Flowamaz-Timestamp", timestamp);
            request.Headers.Add("X-Flowamaz-Signature", $"sha256={Sign($"{timestamp}.{payloadJson}", credential.Secret)}");
        }

        var result = await ConnectorHttp.SendAsync(client, request, op, ct);
        _logger.LogInformation("[WebhookEmit:send-webhook] ExecuteAsync exit");
        return result;
    }

    internal static string Sign(string message, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
}
