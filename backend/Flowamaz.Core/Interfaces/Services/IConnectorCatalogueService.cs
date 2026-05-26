using Flowamaz.Core.Entities.Connector;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Manages the connector catalogue: browsing all available connectors and
/// installing/uninstalling them per workspace.
/// </summary>
public interface IConnectorCatalogueService
{
    /// <summary>Returns all non-deleted connector definitions available on the platform.</summary>
    Task<IReadOnlyList<ConnectorDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a single connector definition by its database ID.</summary>
    Task<ConnectorDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Installs a connector into <paramref name="workspaceId"/>. Creates the
    /// <see cref="WorkspaceConnector"/> join record.
    /// </summary>
    Task<WorkspaceConnector> InstallAsync(
        Guid workspaceId,
        Guid connectorDefinitionId,
        Guid? credentialId,
        Guid installedBy,
        CancellationToken ct = default);

    /// <summary>Soft-deletes the workspace connector installation.</summary>
    Task UninstallAsync(Guid workspaceId, Guid workspaceConnectorId, CancellationToken ct = default);

    /// <summary>Returns all connectors installed in <paramref name="workspaceId"/>.</summary>
    Task<IReadOnlyList<WorkspaceConnector>> GetInstalledAsync(Guid workspaceId, CancellationToken ct = default);
}
