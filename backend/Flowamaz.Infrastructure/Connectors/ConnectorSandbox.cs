using System.Text;
using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NJsonSchema;

namespace Flowamaz.Infrastructure.Connectors;

/// <summary>
/// Phase 4 HTTP-based connector sandbox.
///
/// Execution flow:
/// 1. Look up connector definition + operation from ManifestJson
/// 2. Validate input against operation's input_schema (NJsonSchema)
/// 3. Retrieve credential from vault
/// 4. Execute HTTP call via HttpClientFactory
/// 5. Validate response against output_schema (NJsonSchema)
/// 6. Return <see cref="ConnectorExecutionResult"/>
/// </summary>
public sealed class ConnectorSandbox : IConnectorSandbox
{
    public const string HttpClientName = "connector-sandbox";

    private readonly FlowAmazDbContext _db;
    private readonly ICredentialVaultService _vaultService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConnectorSandbox> _logger;

    public ConnectorSandbox(
        FlowAmazDbContext db,
        ICredentialVaultService vaultService,
        IHttpClientFactory httpClientFactory,
        ILogger<ConnectorSandbox> logger)
    {
        _db = db;
        _vaultService = vaultService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ConnectorExecutionResult> ExecuteAsync(
        Guid workspaceId,
        string connectorId,
        string operationId,
        JsonDocument input,
        Guid credentialId,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "ConnectorSandbox.ExecuteAsync enter workspace={WorkspaceId} connector={ConnectorId} operation={OperationId}",
            workspaceId, connectorId, operationId);

        try
        {
            // 1. Look up connector definition
            var definition = await _db.ConnectorDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ConnectorId == connectorId && !c.IsDeleted, ct)
                ?? throw new InvalidOperationException(
                    $"Connector '{connectorId}' not found. Install the connector in your workspace before executing it.");

            // 2. Parse manifest and locate operation
            var manifest = JsonDocument.Parse(definition.ManifestJson);
            if (!TryGetOperation(manifest, operationId, out var operationElement))
                throw new InvalidOperationException(
                    $"Operation '{operationId}' not found in connector '{connectorId}'. " +
                    $"Check the connector manifest for available operations.");

            // 3. Validate input schema
            var inputSchemaResult = await ValidateSchemaAsync(input.RootElement, operationElement, "input_schema");
            if (inputSchemaResult is not null)
            {
                _logger.LogWarning(
                    "ConnectorSandbox.ExecuteAsync input validation failed connector={ConnectorId} operation={OperationId} errors={Errors}",
                    connectorId, operationId, inputSchemaResult);
                return new ConnectorExecutionResult(false, null,
                    $"Input validation failed: {inputSchemaResult}. Review the connector's input_schema and correct the input.");
            }

            // 4. Retrieve credential
            string? credentialValue = null;
            if (credentialId != Guid.Empty)
            {
                credentialValue = await _vaultService.RetrieveAsync(workspaceId, credentialId, ct);
            }

            // 5. Execute HTTP call
            var (success, responseJson, errorMessage) = await ExecuteHttpAsync(
                operationElement, input, credentialValue, ct);

            if (!success || responseJson is null)
            {
                return new ConnectorExecutionResult(false, null,
                    errorMessage ?? "HTTP call failed with no response.");
            }

            using var responseDoc = JsonDocument.Parse(responseJson);

            // 6. Validate output schema
            var outputSchemaResult = await ValidateSchemaAsync(responseDoc.RootElement, operationElement, "output_schema");
            if (outputSchemaResult is not null)
            {
                _logger.LogWarning(
                    "ConnectorSandbox.ExecuteAsync output validation failed connector={ConnectorId} operation={OperationId}",
                    connectorId, operationId);
                return new ConnectorExecutionResult(false, null,
                    $"Output validation failed: {outputSchemaResult}. The connector returned unexpected data.");
            }

            var outputDoc = JsonDocument.Parse(responseJson);

            _logger.LogInformation(
                "ConnectorSandbox.ExecuteAsync exit workspace={WorkspaceId} connector={ConnectorId} operation={OperationId} success=true",
                workspaceId, connectorId, operationId);

            return new ConnectorExecutionResult(true, outputDoc, null);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "ConnectorSandbox.ExecuteAsync invalid operation workspace={WorkspaceId} connector={ConnectorId}",
                workspaceId, connectorId);
            return new ConnectorExecutionResult(false, null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ConnectorSandbox.ExecuteAsync error workspace={WorkspaceId} connector={ConnectorId} operation={OperationId}",
                workspaceId, connectorId, operationId);
            return new ConnectorExecutionResult(false, null,
                $"Connector execution failed: {ex.Message}. Check logs for details.");
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool TryGetOperation(JsonDocument manifest, string operationId, out JsonElement operationElement)
    {
        operationElement = default;

        if (!manifest.RootElement.TryGetProperty("operations", out var operations))
            return false;

        foreach (var op in operations.EnumerateArray())
        {
            if (op.TryGetProperty("id", out var idProp) && idProp.GetString() == operationId)
            {
                operationElement = op;
                return true;
            }
        }

        return false;
    }

    private static async Task<string?> ValidateSchemaAsync(
        JsonElement data,
        JsonElement operationElement,
        string schemaPropertyName)
    {
        if (!operationElement.TryGetProperty(schemaPropertyName, out var schemaElement))
            return null; // no schema = valid by default

        var schemaJson = schemaElement.GetRawText();
        var schema = await JsonSchema.FromJsonAsync(schemaJson);
        var errors = schema.Validate(data.GetRawText());

        if (errors.Count == 0) return null;

        return string.Join("; ", errors.Select(e => e.ToString()));
    }

    private async Task<(bool success, string? responseJson, string? error)> ExecuteHttpAsync(
        JsonElement operationElement,
        JsonDocument input,
        string? credentialValue,
        CancellationToken ct)
    {
        if (!operationElement.TryGetProperty("http", out var httpConfig))
        {
            return (false, null, "Operation has no 'http' configuration in manifest. Only HTTP operations are supported in Phase 4.");
        }

        var method = httpConfig.TryGetProperty("method", out var methodProp)
            ? methodProp.GetString() ?? "GET"
            : "GET";

        var urlTemplate = httpConfig.TryGetProperty("url", out var urlProp)
            ? urlProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(urlTemplate))
            return (false, null, "Operation HTTP config has no 'url'. Check the connector manifest.");

        try
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(new HttpMethod(method), urlTemplate);

            // Apply credential as Bearer token if available
            if (!string.IsNullOrEmpty(credentialValue))
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {credentialValue}");
            }

            // Set request body for POST/PUT/PATCH
            if (method is "POST" or "PUT" or "PATCH")
            {
                var body = input.RootElement.GetRawText();
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var response = await httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return (false, null,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from connector endpoint. " +
                    $"Verify the URL, credentials, and input parameters.");
            }

            return (true, responseBody, null);
        }
        catch (HttpRequestException ex)
        {
            return (false, null,
                $"Network error calling connector endpoint: {ex.Message}. " +
                "Check the URL is reachable and the credentials are valid.");
        }
    }
}
