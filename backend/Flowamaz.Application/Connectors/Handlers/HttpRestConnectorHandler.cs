using System.Text;
using System.Text.Json;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Base helper for HTTP-REST connector handlers. Each HTTP method gets its own subclass so the
/// registry can look up by (connectorId, operationId).
/// Input schema: { url: string, headers?: object, body?: string, timeout_seconds?: int }
/// </summary>
public abstract class HttpRestConnectorHandlerBase : IConnectorOperationHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    protected HttpRestConnectorHandlerBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ConnectorId => "http-rest";
    public abstract string OperationId { get; }

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        _logger.LogInformation("[HttpRest:{Op}] ExecuteAsync entry", OperationId);

        var root = input.RootElement;
        var url = root.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("Input property 'url' is required.");
        var timeoutSeconds = root.TryGetProperty("timeout_seconds", out var tEl) && tEl.TryGetInt32(out var t) ? t : 30;

        using var client = _httpClientFactory.CreateClient("http-rest-connector");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        using var request = new HttpRequestMessage(new HttpMethod(OperationId), url);

        if (root.TryGetProperty("headers", out var headersEl) && headersEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var hdr in headersEl.EnumerateObject())
                request.Headers.TryAddWithoutValidation(hdr.Name, hdr.Value.GetString());
        }

        if (root.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind != JsonValueKind.Null)
        {
            var bodyStr = bodyEl.ValueKind == JsonValueKind.String
                ? bodyEl.GetString() ?? string.Empty
                : bodyEl.GetRawText();
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var statusCode = (int)response.StatusCode;

        _logger.LogInformation("[HttpRest:{Op}] ExecuteAsync exit status={Status}", OperationId, statusCode);

        if (string.IsNullOrWhiteSpace(responseBody))
            return JsonDocument.Parse($"{{\"status_code\":{statusCode},\"body\":null}}");

        try
        {
            return JsonDocument.Parse($"{{\"status_code\":{statusCode},\"body\":{responseBody}}}");
        }
        catch
        {
            var escaped = JsonSerializer.Serialize(responseBody);
            return JsonDocument.Parse($"{{\"status_code\":{statusCode},\"body\":{escaped}}}");
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
