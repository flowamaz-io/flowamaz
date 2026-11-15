using System.Text.Json;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// One operation of one connector (e.g. slack/send-message). Handlers are resolved by
/// (<see cref="ConnectorId"/>, <see cref="OperationId"/>) through <see cref="IConnectorOperationHandlerRegistry"/>
/// and invoked by the workflow engine for action nodes and by human-gate delivery.
///
/// Contract:
/// - <c>input</c> holds only the node's declared inputs, already variable-interpolated.
/// - <c>credential</c> is the decrypted workspace credential (null when none is configured; whether
///   one is required is declared in the connector catalog/manifest).
///   Secrets must come from the credential, never from input.
/// - Vendor or input failures throw <see cref="Flowamaz.Core.Exceptions.ConnectorOperationException"/>
///   with an actionable message; a returned document always means success.
/// </summary>
public interface IConnectorOperationHandler
{
    string ConnectorId { get; }
    string OperationId { get; }

    Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct);
}
