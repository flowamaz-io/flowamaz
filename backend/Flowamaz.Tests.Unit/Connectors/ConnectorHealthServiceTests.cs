using FluentAssertions;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace Flowamaz.Tests.Unit.Connectors;

[Trait("Category", "Connectors")]
public class ConnectorHealthServiceTests
{
    private static (Mock<IConnectionMultiplexer>, Mock<IDatabase>) BuildRedisMocks(int callCount = 0)
    {
        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)callCount.ToString());

        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
        return (multiplexerMock, dbMock);
    }

    private static ConnectorDefinition BuildDef(string connectorId) => new()
    {
        Id = Guid.NewGuid(),
        ConnectorId = connectorId,
        DisplayName = connectorId,
        PublisherId = "flowamaz-io",
        Version = "1.0.0",
        Category = "generic"
    };

    private static WorkspaceConnector BuildInstalled(Guid workspaceId, ConnectorDefinition def, Guid credId) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ConnectorDefinitionId = def.Id,
        ConnectorDefinition = def,
        CredentialId = credId,
        InstalledBy = Guid.NewGuid()
    };

    [Fact]
    public async Task GetHealthAsync_ExpiredCredential_ReturnsExpiredStatus()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var credId = Guid.NewGuid();
        var def = BuildDef("slack");
        var installed = new List<WorkspaceConnector> { BuildInstalled(workspaceId, def, credId) };

        var expiredCredential = new CredentialAlias(
            credId, "slack-cred", "OAuth2AuthCode",
            ExpiresAt: DateTime.UtcNow.AddDays(-1), // expired yesterday
            IsActive: true);

        var catalogueMock = new Mock<IConnectorCatalogueService>();
        catalogueMock.Setup(c => c.GetInstalledAsync(workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(installed);

        var vaultMock = new Mock<ICredentialVaultService>();
        vaultMock.Setup(v => v.ListAsync(workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CredentialAlias> { expiredCredential });

        var (multiplexerMock, _) = BuildRedisMocks();
        var service = new ConnectorHealthService(
            catalogueMock.Object, vaultMock.Object, multiplexerMock.Object, NullLogger<ConnectorHealthService>.Instance);

        // Act
        var result = await service.GetHealthAsync(workspaceId);

        // Assert
        result.Should().HaveCount(1);
        result[0].Status.Should().Be("expired");
        result[0].ConnectorId.Should().Be("slack");
        result[0].ErrorMessage.Should().Contain("expired");
    }

    [Fact]
    public async Task GetHealthAsync_ActiveCredentialWithFutureExpiry_ReturnsHealthyStatus()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var credId = Guid.NewGuid();
        var def = BuildDef("github");
        var installed = new List<WorkspaceConnector> { BuildInstalled(workspaceId, def, credId) };

        var healthyCredential = new CredentialAlias(
            credId, "github-cred", "ApiKey",
            ExpiresAt: DateTime.UtcNow.AddDays(30), // expires in future
            IsActive: true);

        var catalogueMock = new Mock<IConnectorCatalogueService>();
        catalogueMock.Setup(c => c.GetInstalledAsync(workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(installed);

        var vaultMock = new Mock<ICredentialVaultService>();
        vaultMock.Setup(v => v.ListAsync(workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CredentialAlias> { healthyCredential });

        var (multiplexerMock, _) = BuildRedisMocks(callCount: 5);
        var service = new ConnectorHealthService(
            catalogueMock.Object, vaultMock.Object, multiplexerMock.Object, NullLogger<ConnectorHealthService>.Instance);

        // Act
        var result = await service.GetHealthAsync(workspaceId);

        // Assert
        result.Should().HaveCount(1);
        result[0].Status.Should().Be("healthy");
        result[0].ConnectorId.Should().Be("github");
        result[0].CallsLastHour.Should().Be(5);
    }
}
