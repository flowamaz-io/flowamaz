namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Resolves connector operation handlers by (connectorId, operationId).
/// </summary>
public interface IConnectorOperationHandlerRegistry
{
    IConnectorOperationHandler? Get(string connectorId, string operationId);
}
