using System.Net.Http;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Workers;

/// <summary>
/// Executes Action nodes that call external HTTP APIs. Uses <see cref="IHttpClientFactory"/> (never
/// <c>new HttpClient()</c>), substitutes <c>{{ vars }}</c> in URL/headers/body, stamps an idempotency
/// header, enforces the node TimeoutPolicy via a linked CTS, and retries transient failures (5xx /
/// timeout / network) with exponential backoff per the node RetryPolicy. Credential resolution from
/// the vault arrives in Phase 4 — for now auth headers come straight from node config.
/// </summary>
public sealed class HttpActionWorker : INodeWorker
{
    public const string HttpClientName = "workflow-http";
    private const int DefaultTimeoutSeconds = 30;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IVariableEvaluationService _variableEvaluation;
    private readonly ILogger<HttpActionWorker> _logger;

    public HttpActionWorker(
        IHttpClientFactory httpClientFactory,
        IVariableEvaluationService variableEvaluation,
        ILogger<HttpActionWorker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _variableEvaluation = variableEvaluation;
        _logger = logger;
    }

    public NodeType SupportedType => NodeType.Action;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "HttpActionWorker.ExecuteAsync enter instance={InstanceId} node={NodeId}", context.InstanceId, context.Node.Id);

        try
        {
            var config = context.Node.Config.RootElement;
            var method = new HttpMethod((GetString(config, "method") ?? "GET").ToUpperInvariant());
            var urlTemplate = GetString(config, "url");
            if (string.IsNullOrWhiteSpace(urlTemplate))
            {
                return NodeExecutionResult.Fatal(
                    $"Action node '{context.Node.Id}' has no 'url' in its config. Add a url to call.");
            }

            var url = await _variableEvaluation.InterpolateAsync(context.InstanceId, urlTemplate, cancellationToken);
            var bodyTemplate = GetString(config, "body");
            var body = bodyTemplate is null
                ? null
                : await _variableEvaluation.InterpolateAsync(context.InstanceId, bodyTemplate, cancellationToken);

            var retry = context.Node.RetryPolicy;
            var maxAttempts = Math.Max(1, retry?.MaxAttempts ?? 1);
            var timeoutSeconds = context.Node.TimeoutPolicy?.TimeoutSeconds ?? DefaultTimeoutSeconds;
            var client = _httpClientFactory.CreateClient(HttpClientName);

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    using var request = BuildRequest(method, url, config, body, context);
                    using var response = await client.SendAsync(request, timeoutCts.Token);

                    if ((int)response.StatusCode >= 500)
                    {
                        if (attempt < maxAttempts)
                        {
                            await BackoffAsync(retry, attempt, cancellationToken);
                            continue;
                        }
                        _logger.LogWarning(
                            "HttpActionWorker.ExecuteAsync node={NodeId} exhausted retries on HTTP {Status}",
                            context.Node.Id, (int)response.StatusCode);
                        return NodeExecutionResult.Fatal($"HTTP {(int)response.StatusCode} after {attempt} attempt(s).");
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
                    // Timeout (the linked CTS fired, not the caller). Timeouts are non-retryable from
                    // the worker's perspective per acceptance criteria.
                    _logger.LogWarning(
                        "HttpActionWorker.ExecuteAsync node={NodeId} timed out after {Timeout}s", context.Node.Id, timeoutSeconds);
                    return NodeExecutionResult.Fatal($"Request to '{url}' timed out after {timeoutSeconds}s.");
                }
                catch (HttpRequestException ex)
                {
                    if (attempt < maxAttempts)
                    {
                        await BackoffAsync(retry, attempt, cancellationToken);
                        continue;
                    }
                    _logger.LogWarning(ex, "HttpActionWorker.ExecuteAsync node={NodeId} network error after {Attempts} attempt(s)", context.Node.Id, attempt);
                    return NodeExecutionResult.Fatal($"Network error calling '{url}': {ex.Message}");
                }
            }

            return NodeExecutionResult.Fatal("HTTP action exhausted all attempts without a response.");
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

    private static async Task BackoffAsync(Core.Workflow.RetryPolicy? retry, int attempt, CancellationToken ct)
    {
        if (retry is null) return;
        var seconds = retry.BackoffSeconds * Math.Pow(retry.BackoffMultiplier <= 0 ? 1 : retry.BackoffMultiplier, attempt - 1);
        if (seconds <= 0) return;
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
