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

    /// <summary>
    /// Connector definitions <paramref name="workspaceId"/> may see: platform-wide ones and its own private ones
    /// (never another workspace's). Older majors are included; <c>IsLatestMajor</c> marks the one offered to installs.
    /// </summary>
    Task<IReadOnlyList<ConnectorDefinition>> GetVisibleAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>
    /// The definition a workspace runs for <paramref name="connectorId"/>: the version it installed (its pinned
    /// major) when installed, otherwise the latest visible major. Null when the workspace can't see the connector.
    /// </summary>
    Task<ConnectorDefinition?> ResolveForWorkspaceAsync(Guid workspaceId, string connectorId, CancellationToken ct = default);

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
