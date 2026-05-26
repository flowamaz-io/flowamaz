using System.Text.Json;
using Flowamaz.Core.Entities.Connector;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Handles execution of a specific operation on a specific connector.
/// One implementation per (ConnectorId, OperationId) pair.
/// </summary>
public interface IConnectorOperationHandler
{
    string ConnectorId { get; }
    string OperationId { get; }
    Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct);
}
