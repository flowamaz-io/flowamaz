using System.Text.Json;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>Result of a sandboxed connector operation execution.</summary>
public record ConnectorExecutionResult(
    bool Success,
    JsonDocument? Output,
    string? ErrorMessage);

/// <summary>
/// Executes a connector operation in an isolated context: validates input/output schemas,
/// retrieves credentials from the vault, and dispatches the HTTP call.
/// </summary>
public interface IConnectorSandbox
{
    /// <summary>
    /// Execute a single connector operation. Validates input schema, retrieves credential,
    /// calls the HTTP endpoint, validates output schema, and returns the result.
    /// </summary>
    Task<ConnectorExecutionResult> ExecuteAsync(
        Guid workspaceId,
        string connectorId,
        string operationId,
        JsonDocument input,
        Guid credentialId,
        CancellationToken ct = default);
}
