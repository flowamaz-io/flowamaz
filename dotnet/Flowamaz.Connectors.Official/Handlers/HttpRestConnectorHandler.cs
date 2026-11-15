using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Flowamaz.Application.Connectors.Security;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Generic HTTP connector. One subclass per verb so the registry resolves (http-rest, VERB).
/// Input: { url, headers?, body?, timeout_seconds?, api_key_header? }.
/// Auth comes from the optional credential: BearerToken/OAuth → Authorization: Bearer,
/// BasicAuth ("user:password") → Authorization: Basic, ApiKey → api_key_header (default X-API-Key).
/// Output: { status_code, body }. Non-2xx responses fail the step (429/5xx retryable).
/// The named client goes through <see cref="EgressGuard"/>.
/// </summary>
public abstract class HttpRestConnectorHandlerBase : IConnectorOperationHandler
{
    public const string HttpClientName = "http-rest-connector";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    protected HttpRestConnectorHandlerBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "http-rest";
    public abstract string OperationId { get; }

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[HttpRest:{Op}] ExecuteAsync entry", OperationId);
        var op = $"http-rest/{OperationId}";
        var root = input.RootElement;
        var url = EgressGuard.RequireHttpUrl(ConnectorInput.Optional(root, "url"));
        var timeoutSeconds = ConnectorInput.Int(root, "timeout_seconds", 30, 1, 120);

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        using var request = new HttpRequestMessage(new HttpMethod(OperationId), url);

        if (root.TryGetProperty("headers", out var headersEl) && headersEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var hdr in headersEl.EnumerateObject())
            {
                if (hdr.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && credential is not null) continue;
                var value = hdr.Value.ValueKind == JsonValueKind.String ? hdr.Value.GetString() : hdr.Value.GetRawText();
                request.Headers.TryAddWithoutValidation(hdr.Name, value);
            }
        }

        ApplyCredential(request, credential, ConnectorInput.Optional(root, "api_key_header"));

        if (root.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            var bodyStr = bodyEl.ValueKind == JsonValueKind.String ? bodyEl.GetString() ?? string.Empty : bodyEl.GetRawText();
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorOperationException($"{op}: could not reach {url.Host}: {ex.Message}", retryable: true, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConnectorOperationException($"{op}: {url.Host} did not respond within {timeoutSeconds}s.", retryable: true, ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("[HttpRest:{Op}] ExecuteAsync exit status={Status}", OperationId, status);

            if (!response.IsSuccessStatusCode)
            {
                var detail = text.Length > 300 ? text[..300] : text;
                throw new ConnectorOperationException($"{op}: {url.Host} returned HTTP {status}. Detail: {detail}",
                    ConnectorOperationException.IsRetryableStatus(status));
            }

            object? body = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { body = JsonDocument.Parse(text).RootElement.Clone(); }
                catch (JsonException) { body = text; }
            }
            return ConnectorHttp.ToDocument(new { status_code = status, body });
        }
    }

    internal static void ApplyCredential(HttpRequestMessage request, ConnectorCredential? credential, string? apiKeyHeader)
    {
        if (credential is null) return;
        switch (credential.AuthType)
        {
            case "BasicAuth":
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.Secret)));
                break;
            case "ApiKey":
                request.Headers.Remove(apiKeyHeader ?? "X-API-Key");
                request.Headers.TryAddWithoutValidation(string.IsNullOrWhiteSpace(apiKeyHeader) ? "X-API-Key" : apiKeyHeader, credential.Secret);
                break;
            default: // BearerToken, OAuth2*
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
                break;
        }
    }
}

public sealed class HttpGetHandler : HttpRestConnectorHandlerBase
{
    public HttpGetHandler(IHttpClientFactory f, ILogger<HttpGetHandler> l) : base(f, l) { }
    public override string OperationId => "GET";
}

public sealed class HttpPostHandler : HttpRestConnectorHandlerBase
{
    public HttpPostHandler(IHttpClientFactory f, ILogger<HttpPostHandler> l) : base(f, l) { }
    public override string OperationId => "POST";
}

public sealed class HttpPutHandler : HttpRestConnectorHandlerBase
{
    public HttpPutHandler(IHttpClientFactory f, ILogger<HttpPutHandler> l) : base(f, l) { }
    public override string OperationId => "PUT";
}

public sealed class HttpPatchHandler : HttpRestConnectorHandlerBase
{
    public HttpPatchHandler(IHttpClientFactory f, ILogger<HttpPatchHandler> l) : base(f, l) { }
    public override string OperationId => "PATCH";
}

public sealed class HttpDeleteHandler : HttpRestConnectorHandlerBase
{
    public HttpDeleteHandler(IHttpClientFactory f, ILogger<HttpDeleteHandler> l) : base(f, l) { }
    public override string OperationId => "DELETE";
}

public sealed class HttpHeadHandler : HttpRestConnectorHandlerBase
{
    public HttpHeadHandler(IHttpClientFactory f, ILogger<HttpHeadHandler> l) : base(f, l) { }
    public override string OperationId => "HEAD";
}
