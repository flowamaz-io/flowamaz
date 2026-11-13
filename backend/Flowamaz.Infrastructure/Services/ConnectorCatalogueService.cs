using Flowamaz.Core.Exceptions;
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

    public async Task<IReadOnlyList<ConnectorDefinition>> GetVisibleAsync(Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug("ConnectorCatalogueService.GetVisibleAsync enter workspace={WorkspaceId}", workspaceId);
        try
        {
            var definitions = await _db.ConnectorDefinitions.AsNoTracking()
                .Where(c => c.WorkspaceId == null || c.WorkspaceId == workspaceId)
                .OrderBy(c => c.DisplayName).ThenByDescending(c => c.MajorVersion)
                .ToListAsync(ct);
            _logger.LogInformation("ConnectorCatalogueService.GetVisibleAsync exit workspace={WorkspaceId} count={Count}", workspaceId, definitions.Count);
            return definitions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConnectorCatalogueService.GetVisibleAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task<ConnectorDefinition?> ResolveForWorkspaceAsync(Guid workspaceId, string connectorId, CancellationToken ct = default)
    {
        _logger.LogDebug("ConnectorCatalogueService.ResolveForWorkspaceAsync enter workspace={WorkspaceId} connector={ConnectorId}", workspaceId, connectorId);
        try
        {
            var installed = await _db.WorkspaceConnectors.AsNoTracking()
                .Where(wc => wc.WorkspaceId == workspaceId && wc.ConnectorDefinition!.ConnectorId == connectorId)
                .Select(wc => wc.ConnectorDefinition)
                .FirstOrDefaultAsync(ct);
            var definition = installed ?? await _db.ConnectorDefinitions.AsNoTracking()
                .Where(c => c.ConnectorId == connectorId && (c.WorkspaceId == null || c.WorkspaceId == workspaceId))
                .OrderByDescending(c => c.WorkspaceId == workspaceId).ThenByDescending(c => c.IsLatestMajor).ThenByDescending(c => c.MajorVersion)
                .FirstOrDefaultAsync(ct);
            _logger.LogInformation("ConnectorCatalogueService.ResolveForWorkspaceAsync exit connector={ConnectorId} installed={Installed} version={Version}",
                connectorId, installed is not null, definition?.Version);
            return definition;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConnectorCatalogueService.ResolveForWorkspaceAsync error connector={ConnectorId}", connectorId);
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
            // Tracked load: the install count is incremented in the same SaveChanges (one transaction).
            var definition = await _db.ConnectorDefinitions
                .FirstOrDefaultAsync(c => c.Id == connectorDefinitionId && !c.IsDeleted, ct)
                ?? throw new InvalidOperationException(
                    $"Connector definition '{connectorDefinitionId}' not found. " +
                    "Ensure the connector exists in the catalogue before installing.");

            if (!definition.IsEnabled)
                throw LibraryException.Disabled(definition.DisplayName);

            var existing = await _db.WorkspaceConnectors
                .FirstOrDefaultAsync(wc =>
                    wc.WorkspaceId == workspaceId &&
                    wc.ConnectorDefinitionId == connectorDefinitionId &&
                    !wc.IsDeleted, ct);

            if (existing is not null)
                throw LibraryException.AlreadyInstalled(definition.DisplayName,
                    $"{definition.PublisherId}/{definition.ConnectorId}@{definition.MajorVersion}", newerMajor: false);

            var connector = new WorkspaceConnector
            {
                WorkspaceId = workspaceId,
                ConnectorDefinitionId = connectorDefinitionId,
                CredentialId = credentialId,
                IsEnabled = true,
                InstalledAt = DateTime.UtcNow,
                InstalledBy = installedBy,
                InstalledVersion = definition.Version,
            };

            await _db.WorkspaceConnectors.AddAsync(connector, ct);
            definition.InstallCount += 1;
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

            // Decrement the install count in the same transaction (never below zero).
            var definition = await _db.ConnectorDefinitions
                .FirstOrDefaultAsync(c => c.Id == connector.ConnectorDefinitionId, ct);
            if (definition is { InstallCount: > 0 })
                definition.InstallCount -= 1;

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
