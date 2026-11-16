using System.Net.Http;
using System.Text;
using System.Text.Json;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Workers;

/// <summary>
/// Executes Action nodes. Nodes that reference a connector (<c>connector_id</c>/<c>operation</c>, or the
/// legacy canvas keys) are dispatched to <see cref="ConnectorActionExecutor"/> — handler, vault
/// credential, interpolated inputs. Nodes with a raw <c>url</c> are called directly below.
///
/// Raw HTTP: executes Action nodes that call external HTTP APIs. Uses <see cref="IHttpClientFactory"/> (never
/// <c>new HttpClient()</c>), substitutes <c>{{ vars }}</c> in URL/headers/body, stamps an idempotency
/// header and enforces the node TimeoutPolicy via a linked CTS. It makes one attempt per call: a
/// transient failure (5xx or a network error) is returned as retryable, and the orchestrator retries
/// it durably per the node RetryPolicy (backoff on the delayed queue, so it survives a restart and
/// doesn't hold the worker). 4xx and timeouts fail at once. Auth headers come from node config.
/// </summary>
public sealed class HttpActionWorker : INodeWorker
{
    public const string HttpClientName = "workflow-http";
    private const int DefaultTimeoutSeconds = 30;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IVariableEvaluationService _variableEvaluation;
    private readonly ILogger<HttpActionWorker> _logger;
    private readonly ConnectorActionExecutor? _connectors;

    public HttpActionWorker(
        IHttpClientFactory httpClientFactory,
        IVariableEvaluationService variableEvaluation,
        ILogger<HttpActionWorker> logger,
        ConnectorActionExecutor? connectors = null)
    {
        _httpClientFactory = httpClientFactory;
        _variableEvaluation = variableEvaluation;
        _logger = logger;
        _connectors = connectors;
    }

    public NodeType SupportedType => NodeType.Action;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "HttpActionWorker.ExecuteAsync enter instance={InstanceId} node={NodeId}", context.InstanceId, context.Node.Id);

        try
        {
            var config = context.Node.Config.RootElement;

            if (ConnectorNodeConfig.TryParse(config) is { } connectorNode)
            {
                if (_connectors is null)
                    return NodeExecutionResult.Fatal("Connector execution is not available in this host.");
                return await _connectors.ExecuteAsync(context, connectorNode, cancellationToken);
            }

            var method = new HttpMethod((GetString(config, "method") ?? "GET").ToUpperInvariant());
            var urlTemplate = GetString(config, "url");
            if (string.IsNullOrWhiteSpace(urlTemplate))
            {
                return NodeExecutionResult.Fatal(
                    $"Action node '{context.Node.Id}' has no connector or url configured. Open the node config and select a connector and action.");
            }

            var url = await _variableEvaluation.InterpolateAsync(context.InstanceId, urlTemplate, cancellationToken);
            var bodyTemplate = GetString(config, "body");
            var body = bodyTemplate is null
                ? null
                : await _variableEvaluation.InterpolateAsync(context.InstanceId, bodyTemplate, cancellationToken);

            var timeoutSeconds = context.Node.TimeoutPolicy?.TimeoutSeconds is > 0 and var configured
                ? configured
                : DefaultTimeoutSeconds;
            var client = _httpClientFactory.CreateClient(HttpClientName);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                using var request = BuildRequest(method, url, config, body, context);
                using var response = await client.SendAsync(request, timeoutCts.Token);

                if ((int)response.StatusCode >= 500)
                {
                    var detail = await SafeReadAsync(response);
                    _logger.LogWarning(
                        "HttpActionWorker.ExecuteAsync node={NodeId} got HTTP {Status} (retryable)",
                        context.Node.Id, (int)response.StatusCode);
                    return NodeExecutionResult.Retryable(
                        $"'{url}' returned HTTP {(int)response.StatusCode}: {detail}. The step is retried if it has a retry policy.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await SafeReadAsync(response);
                    return NodeExecutionResult.Fatal($"HTTP {(int)response.StatusCode}: {error}");
                }

                var output = await ParseResponseAsync(response, (int)response.StatusCode);
                _logger.LogInformation(
                    "HttpActionWorker.ExecuteAsync exit instance={InstanceId} node={NodeId} status={Status}",
                    context.InstanceId, context.Node.Id, (int)response.StatusCode);
                return NodeExecutionResult.Ok(output);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout (the linked CTS fired, not the caller): not retried, per the acceptance criteria.
                _logger.LogWarning(
                    "HttpActionWorker.ExecuteAsync node={NodeId} timed out after {Timeout}s", context.Node.Id, timeoutSeconds);
                return NodeExecutionResult.Fatal($"Request to '{url}' timed out after {timeoutSeconds}s.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HttpActionWorker.ExecuteAsync node={NodeId} network error (retryable)", context.Node.Id);
                return NodeExecutionResult.Retryable($"Network error calling '{url}': {ex.Message}. The step is retried if it has a retry policy.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "HttpActionWorker.ExecuteAsync error instance={InstanceId} node={NodeId}", context.InstanceId, context.Node.Id);
            return NodeExecutionResult.Fatal($"Unexpected error executing action '{context.Node.Id}': {ex.Message}");
        }
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method, string url, JsonElement config, string? body, NodeExecutionContext context)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", $"{context.InstanceId}-{context.Node.Id}");

        if (config.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
        {
            foreach (var header in headers.EnumerateObject())
            {
                var value = header.Value.ValueKind == JsonValueKind.String ? header.Value.GetString() : header.Value.GetRawText();
                if (value is not null) request.Headers.TryAddWithoutValidation(header.Name, value);
            }
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task<JsonDocument> ParseResponseAsync(HttpResponseMessage response, int statusCode)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return JsonDocument.Parse($$"""{"statusCode":{{statusCode}}}""");
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(new { statusCode, body = content }));
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync();
            return content.Length > 500 ? content[..500] : content;
        }
        catch (HttpRequestException)
        {
            return response.ReasonPhrase ?? "no detail";
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
