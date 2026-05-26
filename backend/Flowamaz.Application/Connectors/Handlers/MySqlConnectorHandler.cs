using System.Text.Json;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Stub handler for MySQL. Returns an error payload — MySQL requires the MySqlConnector package
/// which is not included in this deployment. Configure in Phase 4 infrastructure if needed.
/// </summary>
public sealed class MySqlQueryHandler : IConnectorOperationHandler
{
    private readonly ILogger<MySqlQueryHandler> _logger;

    public MySqlQueryHandler(ILogger<MySqlQueryHandler> logger)
    {
        _logger = logger;
    }

    public string ConnectorId => "mysql";
    public string OperationId => "query";

    public Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        _logger.LogWarning("[MySQL:query] MySQL connector is not available in this deployment.");
        return Task.FromResult(JsonDocument.Parse("{\"error\":\"MySQL connector not available in this deployment\"}"));
    }
}

public sealed class MySqlExecuteHandler : IConnectorOperationHandler
{
    private readonly ILogger<MySqlExecuteHandler> _logger;

    public MySqlExecuteHandler(ILogger<MySqlExecuteHandler> logger)
    {
        _logger = logger;
    }

    public string ConnectorId => "mysql";
    public string OperationId => "execute";

    public Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        _logger.LogWarning("[MySQL:execute] MySQL connector is not available in this deployment.");
        return Task.FromResult(JsonDocument.Parse("{\"error\":\"MySQL connector not available in this deployment\"}"));
    }
}
