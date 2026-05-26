using FluentAssertions;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Connectors;

/// <summary>
/// Unit tests for <see cref="CredentialVaultService"/>.
/// Uses EF In-Memory provider — no PostgreSQL required.
/// </summary>
[Trait("Category", "Connectors")]
public class CredentialVaultServiceTests
{
    private const string MasterKey = "test-master-key-super-secret-32ch!";

    private static FlowAmazDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"vault-{Guid.NewGuid():N}")
            .Options);

    private static CredentialVaultService NewService(FlowAmazDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CREDENTIAL_MASTER_KEY"] = MasterKey
            })
            .Build();

        return new CredentialVaultService(db, config, NullLogger<CredentialVaultService>.Instance);
    }

    private static ConnectorDefinition HttpConnector(Guid id) => new()
    {
        Id = id,
        ConnectorId = "http-rest",
        PublisherId = "flowamaz-io",
        DisplayName = "HTTP/REST",
        Version = "1.0.0",
        Category = "generic",
        Tags = ["http", "rest"],
        ManifestJson = "{}",
        Tier = ConnectorTier.Official,
        IsEnabled = true,
        IsInstalled = false,
        WorkspaceId = null
    };

    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreAndRetrieve_RoundTrip_ReturnsSamePlaintext()
    {
        // Arrange
        var db = NewDb();
        var connectorId = Guid.NewGuid();
        db.ConnectorDefinitions.Add(HttpConnector(connectorId));
        await db.SaveChangesAsync();

        var service = NewService(db);
        var workspaceId = Guid.NewGuid();
        var secretValue = "super-secret-api-key-abc123";

        // Act
        var credId = await service.StoreAsync(workspaceId, "http-rest", "My API Key", "ApiKey", secretValue);
        var retrieved = await service.RetrieveAsync(workspaceId, credId);

        // Assert
        retrieved.Should().Be(secretValue);
    }

    [Fact]
    public async Task List_DoesNotExposeEncryptedValue()
    {
        // Arrange
        var db = NewDb();
        var connectorId = Guid.NewGuid();
        db.ConnectorDefinitions.Add(HttpConnector(connectorId));
        await db.SaveChangesAsync();

        var service = NewService(db);
        var workspaceId = Guid.NewGuid();

        await service.StoreAsync(workspaceId, "http-rest", "Key1", "ApiKey", "plain-secret-1");
        await service.StoreAsync(workspaceId, "http-rest", "Key2", "BearerToken", "plain-secret-2");

        // Act
        var aliases = await service.ListAsync(workspaceId);

        // Assert — only alias fields; no EncryptedValue, EncryptedKey, IV, Tag
        aliases.Should().HaveCount(2);
        aliases.Should().AllSatisfy(a =>
        {
            a.Id.Should().NotBeEmpty();
            a.Name.Should().NotBeNullOrEmpty();
            a.AuthType.Should().NotBeNullOrEmpty();
            a.IsActive.Should().BeTrue();
        });

        // Confirm CredentialAlias record has ONLY the alias fields (compile-time proof via record shape)
        var alias = aliases[0];
        var aliasType = alias.GetType();
        aliasType.GetProperty("EncryptedValue").Should().BeNull();
        aliasType.GetProperty("EncryptedKey").Should().BeNull();
        aliasType.GetProperty("IV").Should().BeNull();
        aliasType.GetProperty("Tag").Should().BeNull();
    }

    [Fact]
    public async Task Retrieve_CrossWorkspace_Throws()
    {
        // Arrange
        var db = NewDb();
        var connectorId = Guid.NewGuid();
        db.ConnectorDefinitions.Add(HttpConnector(connectorId));
        await db.SaveChangesAsync();

        var service = NewService(db);
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();

        var credId = await service.StoreAsync(workspaceA, "http-rest", "A's key", "ApiKey", "workspace-a-secret");

        // Act & Assert — workspace B cannot retrieve workspace A's credential
        var act = async () => await service.RetrieveAsync(workspaceB, credId);
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*does not belong to workspace*");
    }

    [Fact]
    public async Task StoreAndRetrieve_DifferentWorkspaces_EachGetsOwnPlaintext()
    {
        // Arrange
        var db = NewDb();
        var connectorId = Guid.NewGuid();
        db.ConnectorDefinitions.Add(HttpConnector(connectorId));
        await db.SaveChangesAsync();

        var service = NewService(db);
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        const string secretA = "workspace-a-secret-value";
        const string secretB = "workspace-b-secret-value";

        var credA = await service.StoreAsync(workspaceA, "http-rest", "Key A", "ApiKey", secretA);
        var credB = await service.StoreAsync(workspaceB, "http-rest", "Key B", "ApiKey", secretB);

        // Act
        var retrievedA = await service.RetrieveAsync(workspaceA, credA);
        var retrievedB = await service.RetrieveAsync(workspaceB, credB);

        // Assert
        retrievedA.Should().Be(secretA);
        retrievedB.Should().Be(secretB);
        retrievedA.Should().NotBe(retrievedB);
    }

    [Fact]
    public async Task Revoke_SetsIsActiveToFalse()
    {
        // Arrange
        var db = NewDb();
        var connectorId = Guid.NewGuid();
        db.ConnectorDefinitions.Add(HttpConnector(connectorId));
        await db.SaveChangesAsync();

        var service = NewService(db);
        var workspaceId = Guid.NewGuid();

        var credId = await service.StoreAsync(workspaceId, "http-rest", "Test Key", "ApiKey", "my-secret");

        // Act
        await service.RevokeAsync(workspaceId, credId);

        // Assert
        var aliases = await service.ListAsync(workspaceId);
        aliases.Should().ContainSingle(a => a.Id == credId && !a.IsActive);
    }
}
