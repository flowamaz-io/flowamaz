using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Manages the connector catalogue: browsing all available connectors and
/// installing/uninstalling them per workspace. Official connectors are visible
/// platform-wide; community connectors are workspace-scoped.
/// </summary>
public sealed class ConnectorCatalogueService : IConnectorCatalogueService
{
    private readonly FlowAmazDbContext _db;
    private readonly ILogger<ConnectorCatalogueService> _logger;

    public ConnectorCatalogueService(FlowAmazDbContext db, ILogger<ConnectorCatalogueService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConnectorDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("ConnectorCatalogueService.GetAllAsync enter");

        try
        {
            var definitions = await _db.ConnectorDefinitions
                .AsNoTracking()
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.DisplayName)
                .ToListAsync(ct);

            _logger.LogInformation(
                "ConnectorCatalogueService.GetAllAsync exit count={Count}", definitions.Count);

            return definitions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConnectorCatalogueService.GetAllAsync error");
            throw;
        }
    }

    public async Task<ConnectorDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("ConnectorCatalogueService.GetByIdAsync enter id={Id}", id);

        try
        {
            var definition = await _db.ConnectorDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

            _logger.LogInformation(
                "ConnectorCatalogueService.GetByIdAsync exit id={Id} found={Found}",
                id, definition is not null);

            return definition;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConnectorCatalogueService.GetByIdAsync error id={Id}", id);
            throw;
        }
    }

    public async Task<WorkspaceConnector> InstallAsync(
        Guid workspaceId,
        Guid connectorDefinitionId,
        Guid? credentialId,
        Guid installedBy,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "ConnectorCatalogueService.InstallAsync enter workspace={WorkspaceId} connector={ConnectorDefinitionId}",
            workspaceId, connectorDefinitionId);

        try
        {
            var definition = await _db.ConnectorDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == connectorDefinitionId && !c.IsDeleted, ct)
                ?? throw new InvalidOperationException(
                    $"Connector definition '{connectorDefinitionId}' not found. " +
                    "Ensure the connector exists in the catalogue before installing.");

            if (!definition.IsEnabled)
                throw new InvalidOperationException(
                    $"Connector '{definition.DisplayName}' is currently disabled. " +
                    "Contact platform support to enable this connector.");

            var existing = await _db.WorkspaceConnectors
                .FirstOrDefaultAsync(wc =>
                    wc.WorkspaceId == workspaceId &&
                    wc.ConnectorDefinitionId == connectorDefinitionId &&
                    !wc.IsDeleted, ct);

            if (existing is not null)
                throw new InvalidOperationException(
                    $"Connector '{definition.DisplayName}' is already installed in this workspace. " +
                    "Uninstall the existing installation before reinstalling.");

            var connector = new WorkspaceConnector
            {
                WorkspaceId = workspaceId,
                ConnectorDefinitionId = connectorDefinitionId,
                CredentialId = credentialId,
                IsEnabled = true,
                InstalledAt = DateTime.UtcNow,
                InstalledBy = installedBy
            };

            await _db.WorkspaceConnectors.AddAsync(connector, ct);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "ConnectorCatalogueService.InstallAsync exit workspace={WorkspaceId} connector={ConnectorDefinitionId} workspaceConnectorId={Id}",
                workspaceId, connectorDefinitionId, connector.Id);

            return connector;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex,
                "ConnectorCatalogueService.InstallAsync error workspace={WorkspaceId} connector={ConnectorDefinitionId}",
                workspaceId, connectorDefinitionId);
            throw;
        }
    }

    public async Task UninstallAsync(Guid workspaceId, Guid workspaceConnectorId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "ConnectorCatalogueService.UninstallAsync enter workspace={WorkspaceId} workspaceConnectorId={Id}",
            workspaceId, workspaceConnectorId);

        try
        {
            var connector = await _db.WorkspaceConnectors
                .FirstOrDefaultAsync(wc =>
                    wc.Id == workspaceConnectorId &&
                    wc.WorkspaceId == workspaceId &&
                    !wc.IsDeleted, ct)
                ?? throw new InvalidOperationException(
                    $"Workspace connector '{workspaceConnectorId}' not found in workspace '{workspaceId}'. " +
                    "Verify the connector is installed in this workspace.");

            connector.IsDeleted = true;
            connector.DeletedAt = DateTime.UtcNow;
            _db.WorkspaceConnectors.Update(connector);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "ConnectorCatalogueService.UninstallAsync exit workspace={WorkspaceId} workspaceConnectorId={Id}",
                workspaceId, workspaceConnectorId);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex,
                "ConnectorCatalogueService.UninstallAsync error workspace={WorkspaceId} workspaceConnectorId={Id}",
                workspaceId, workspaceConnectorId);
            throw;
        }
    }

    public async Task<IReadOnlyList<WorkspaceConnector>> GetInstalledAsync(Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "ConnectorCatalogueService.GetInstalledAsync enter workspace={WorkspaceId}", workspaceId);

        try
        {
            var connectors = await _db.WorkspaceConnectors
                .AsNoTracking()
                .Where(wc => wc.WorkspaceId == workspaceId && !wc.IsDeleted)
                .ToListAsync(ct);

            _logger.LogInformation(
                "ConnectorCatalogueService.GetInstalledAsync exit workspace={WorkspaceId} count={Count}",
                workspaceId, connectors.Count);

            return connectors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ConnectorCatalogueService.GetInstalledAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }
}
