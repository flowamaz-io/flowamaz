using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Services;

/// <summary>
/// Executes a connector operation either in-process or in the isolated connector-runner container
/// (FUNCTIONAL.md §9.5). When <c>Connectors:Sandbox:Url</c> is configured, every connector the catalog
/// marks <c>Sandboxed</c> (anything that calls third parties or runs code) is sent to the runner as an
/// HMAC-signed request carrying only that operation's inputs and its one credential. Other connectors
/// (platform email, database drivers that cannot use an HTTP egress proxy, workspace file area) and all
/// connectors when no runner is configured run in-process behind the in-process egress guard.
/// </summary>
public sealed class ConnectorDispatcher
{
    public const string HttpClientName = "connector-runner";
    public const string UrlKey = "Connectors:Sandbox:Url";
    public const string SecretKey = "Connectors:Sandbox:SharedSecret";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IConnectorOperationHandlerRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConnectorDispatcher> _logger;
    private readonly string? _runnerUrl;
    private readonly string? _secret;

    public ConnectorDispatcher(
        IConnectorOperationHandlerRegistry registry,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ConnectorDispatcher> logger)
    {
        _registry = registry;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _runnerUrl = configuration[UrlKey]?.TrimEnd('/');
        _secret = configuration[SecretKey] ?? configuration["CONNECTOR_RUNNER_SECRET"];
    }

    public bool SandboxEnabled => !string.IsNullOrWhiteSpace(_runnerUrl);

    /// <summary>True when this connector would run in the sandbox container.</summary>
    public bool IsSandboxed(string connectorId) =>
        SandboxEnabled && OfficialConnectorCatalog.FindConnector(connectorId)?.Sandboxed == true;

    /// <summary>True when an in-process handler exists, or the runner will execute the operation.</summary>
    public bool CanExecute(string connectorId, string operationId) =>
        _registry.Get(connectorId, operationId) is not null
        || (IsSandboxed(connectorId) && OfficialConnectorCatalog.Find(connectorId, operationId) is not null);

    public async Task<JsonDocument> ExecuteAsync(
        string connectorId, string operationId, JsonDocument input, ConnectorCredential? credential,
        Guid workspaceId, Guid? instanceId, string? nodeId, TimeSpan timeout, CancellationToken ct)
    {
        if (!IsSandboxed(connectorId))
        {
            var handler = _registry.Get(connectorId, operationId)
                ?? throw new ConnectorOperationException($"No executor is available for {connectorId}/{operationId}.");
            return await handler.ExecuteAsync(input, credential, ct);
        }

        return await ExecuteRemoteAsync(connectorId, operationId, input, credential, workspaceId, instanceId, nodeId, timeout, ct);
    }

    private async Task<JsonDocument> ExecuteRemoteAsync(
        string connectorId, string operationId, JsonDocument input, ConnectorCredential? credential,
        Guid workspaceId, Guid? instanceId, string? nodeId, TimeSpan timeout, CancellationToken ct)
    {
        var op = $"{connectorId}/{operationId}";
        if (string.IsNullOrEmpty(_secret))
            throw new ConnectorOperationException(
                $"{op}: the connector sandbox is configured without a shared secret. Set CONNECTOR_RUNNER_SECRET on the API and the runner.");

        _logger.LogInformation("ConnectorDispatcher: {Op} → sandbox runner workspace={WorkspaceId} node={NodeId}", op, workspaceId, nodeId);

        var request = new ConnectorRunRequest(
            connectorId, operationId, input.RootElement.Clone(),
            credential is null ? null : new ConnectorRunCredential(credential.Id, credential.Name, credential.AuthType, credential.Secret),
            workspaceId, instanceId, nodeId, (int)Math.Clamp(timeout.TotalSeconds, 1, 300),
            OfficialConnectorCatalog.FindConnector(connectorId)?.AllowedDomains);

        var body = JsonSerializer.Serialize(request, Json);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_runnerUrl}/v1/execute")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Add(RunnerSignature.TimestampHeader, ts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        message.Headers.Add(RunnerSignature.SignatureHeader, RunnerSignature.Compute(_secret, ts, body));

        HttpResponseMessage response;
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = timeout + TimeSpan.FromSeconds(10); // runner enforces the operation timeout itself
            response = await client.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "ConnectorDispatcher: runner unreachable for {Op}", op);
            throw new ConnectorOperationException($"{op}: the connector sandbox is not reachable right now. The step will be retried.", retryable: true, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ConnectorDispatcher: runner returned HTTP {Status} for {Op}", (int)response.StatusCode, op);
                throw new ConnectorOperationException(
                    $"{op}: the connector sandbox rejected the request (HTTP {(int)response.StatusCode}). " +
                    (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "Check that CONNECTOR_RUNNER_SECRET matches on the API and the runner and that clocks are in sync."
                        : "The step will be retried."),
                    retryable: response.StatusCode != System.Net.HttpStatusCode.Unauthorized);
            }

            var result = await response.Content.ReadFromJsonAsync<ConnectorRunResponse>(Json, ct)
                ?? throw new ConnectorOperationException($"{op}: the connector sandbox returned an empty response.", retryable: true);

            if (!result.Success)
                throw new ConnectorOperationException(result.Error ?? $"{op} failed in the connector sandbox.", result.Retryable);

            return JsonDocument.Parse(result.Output?.GetRawText() ?? "{}");
        }
    }
}
