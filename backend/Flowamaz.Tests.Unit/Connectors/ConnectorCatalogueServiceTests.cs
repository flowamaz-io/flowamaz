using FluentAssertions;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Connectors;

/// <summary>
/// Unit tests for <see cref="ConnectorCatalogueService"/>.
/// Uses EF In-Memory provider — no PostgreSQL required.
/// </summary>
[Trait("Category", "Connectors")]
public class ConnectorCatalogueServiceTests
{
    private static FlowAmazDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"catalogue-{Guid.NewGuid():N}")
            .Options);

    private static ConnectorCatalogueService NewService(FlowAmazDbContext db) =>
        new(db, NullLogger<ConnectorCatalogueService>.Instance);

    private static ConnectorDefinition MakeDefinition(string connectorId, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        ConnectorId = connectorId,
        PublisherId = "flowamaz-io",
        DisplayName = connectorId,
        Version = "1.0.0",
        Category = "official",
        Tags = [],
        ManifestJson = "{}",
        Tier = ConnectorTier.Official,
        IsEnabled = enabled,
        IsInstalled = false,
        WorkspaceId = null
    };

    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllSeededConnectors()
    {
        // Arrange
        var db = NewDb();
        var connectors = Enumerable.Range(1, 13).Select(i => MakeDefinition($"connector-{i}")).ToList();
        db.ConnectorDefinitions.AddRange(connectors);
        await db.SaveChangesAsync();

        var service = NewService(db);

        // Act
        var result = await service.GetAllAsync();

        // Assert
        result.Should().HaveCount(13);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingConnector_ReturnsCorrectDefinition()
    {
        // Arrange
        var db = NewDb();
        var definition = MakeDefinition("slack");
        db.ConnectorDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var service = NewService(db);

        // Act
        var result = await service.GetByIdAsync(definition.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(definition.Id);
        result.ConnectorId.Should().Be("slack");
    }

    [Fact]
    public async Task GetByIdAsync_UnknownConnector_ReturnsNull()
    {
        // Arrange
        var db = NewDb();
        var service = NewService(db);

        // Act
        var result = await service.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInstalledAsync_WorkspaceWithInstalledConnectors_ReturnsInstalled()
    {
        // Arrange
        var db = NewDb();
        var workspaceId = Guid.NewGuid();
        var def1 = MakeDefinition("slack");
        var def2 = MakeDefinition("github");
        db.ConnectorDefinitions.AddRange(def1, def2);

        db.WorkspaceConnectors.AddRange(
            new WorkspaceConnector { WorkspaceId = workspaceId, ConnectorDefinitionId = def1.Id, IsEnabled = true },
            new WorkspaceConnector { WorkspaceId = workspaceId, ConnectorDefinitionId = def2.Id, IsEnabled = true });
        await db.SaveChangesAsync();

        var service = NewService(db);

        // Act
        var result = await service.GetInstalledAsync(workspaceId);

        // Assert
        result.Should().HaveCount(2);
        result.All(wc => wc.WorkspaceId == workspaceId).Should().BeTrue();
    }

    [Fact]
    public async Task InstallAsync_NotAlreadyInstalled_CreatesWorkspaceConnector()
    {
        // Arrange
        var db = NewDb();
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var definition = MakeDefinition("slack");
        db.ConnectorDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var service = NewService(db);

        // Act
        var result = await service.InstallAsync(workspaceId, definition.Id, null, userId);

        // Assert
        result.WorkspaceId.Should().Be(workspaceId);
        result.ConnectorDefinitionId.Should().Be(definition.Id);
        result.IsEnabled.Should().BeTrue();

        var installed = await db.WorkspaceConnectors
            .FirstOrDefaultAsync(wc => wc.WorkspaceId == workspaceId && wc.ConnectorDefinitionId == definition.Id);
        installed.Should().NotBeNull();
    }

    [Fact]
    public async Task InstallAsync_AlreadyInstalled_ThrowsInvalidOperationException()
    {
        // Arrange
        var db = NewDb();
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var definition = MakeDefinition("slack");
        db.ConnectorDefinitions.Add(definition);
        db.WorkspaceConnectors.Add(new WorkspaceConnector
        {
            WorkspaceId = workspaceId,
            ConnectorDefinitionId = definition.Id,
            IsEnabled = true
        });
        await db.SaveChangesAsync();

        var service = NewService(db);

        // Act
        var act = async () => await service.InstallAsync(workspaceId, definition.Id, null, userId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already installed*");
    }

    [Fact]
    public async Task UninstallAsync_Installed_SoftDeletesWorkspaceConnector()
    {
        // Arrange
        var db = NewDb();
        var workspaceId = Guid.NewGuid();
        var definition = MakeDefinition("github");
        db.ConnectorDefinitions.Add(definition);
        var workspaceConnector = new WorkspaceConnector
        {
            WorkspaceId = workspaceId,
            ConnectorDefinitionId = definition.Id,
            IsEnabled = true
        };
        db.WorkspaceConnectors.Add(workspaceConnector);
        await db.SaveChangesAsync();

        var service = NewService(db);

        // Act
        await service.UninstallAsync(workspaceId, workspaceConnector.Id);

        // Assert — soft-deleted connector is no longer returned by GetInstalledAsync
        var installed = await service.GetInstalledAsync(workspaceId);
        installed.Should().BeEmpty();

        var raw = await db.WorkspaceConnectors.FindAsync(workspaceConnector.Id);
        raw!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task UninstallAsync_NotInstalled_ThrowsInvalidOperationException()
    {
        // Arrange
        var db = NewDb();
        var workspaceId = Guid.NewGuid();
        var service = NewService(db);

        // Act
        var act = async () => await service.UninstallAsync(workspaceId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }
}
