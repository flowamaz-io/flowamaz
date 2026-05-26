using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Services;

/// <summary>
/// Resolves the correct <see cref="IConnectorOperationHandler"/> by (connectorId, operationId).
/// All handlers are registered as IConnectorOperationHandler in DI and injected here.
/// </summary>
public sealed class ConnectorOperationHandlerRegistry : IConnectorOperationHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IConnectorOperationHandler> _handlers;
    private readonly ILogger<ConnectorOperationHandlerRegistry> _logger;

    public ConnectorOperationHandlerRegistry(
        IEnumerable<IConnectorOperationHandler> handlers,
        ILogger<ConnectorOperationHandlerRegistry> logger)
    {
        _logger = logger;
        _handlers = handlers.ToDictionary(h => BuildKey(h.ConnectorId, h.OperationId));
    }

    public IConnectorOperationHandler? Get(string connectorId, string operationId)
    {
        var key = BuildKey(connectorId, operationId);
        if (_handlers.TryGetValue(key, out var handler))
            return handler;

        _logger.LogWarning(
            "[ConnectorRegistry] No handler found for connector={ConnectorId} operation={OperationId}",
            connectorId, operationId);
        return null;
    }

    private static string BuildKey(string connectorId, string operationId) =>
        $"{connectorId.ToLowerInvariant()}::{operationId.ToLowerInvariant()}";
}
