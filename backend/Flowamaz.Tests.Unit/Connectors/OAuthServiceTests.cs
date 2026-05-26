using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace Flowamaz.Tests.Unit.Connectors;

[Trait("Category", "Connectors")]
public class OAuthServiceTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:BaseUrl"] = "https://flowamaz.io",
                ["Connectors:Slack:ClientId"] = "test-slack-client-id",
                ["Connectors:GitHub:ClientId"] = "test-github-client-id",
                ["Connectors:MicrosoftTeams:ClientId"] = "test-teams-client-id"
            })
            .Build();

    private static (Mock<IConnectionMultiplexer> MultiplexerMock, Mock<IDatabase> DbMock) BuildRedisMocks()
    {
        var dbMock = new Mock<IDatabase>();
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
        return (multiplexerMock, dbMock);
    }

    [Fact]
    public async Task InitiateAsync_StoresStateInRedis_AndReturnsAuthorizationUrl()
    {
        // Arrange
        var (multiplexerMock, dbMock) = BuildRedisMocks();
        string? storedKey = null;
        string? storedValue = null;

        dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>(
                (key, value, _, _, _, _) =>
                {
                    storedKey = key.ToString();
                    storedValue = value.ToString();
                })
            .ReturnsAsync(true);

        var vaultMock = new Mock<ICredentialVaultService>();
        var service = new OAuthService(multiplexerMock.Object, vaultMock.Object, BuildConfig(), NullLogger<OAuthService>.Instance);
        var workspaceId = Guid.NewGuid();

        // Act
        var result = await service.InitiateAsync(workspaceId, "slack");

        // Assert
        result.AuthorizationUrl.Should().Contain("slack.com/oauth/v2/authorize");
        result.AuthorizationUrl.Should().Contain("test-slack-client-id");
        result.State.Should().NotBeNullOrEmpty();

        storedKey.Should().Be($"oauth:state:{result.State}");
        storedValue.Should().NotBeNullOrEmpty();

        var payload = JsonDocument.Parse(storedValue!).RootElement;
        payload.GetProperty("WorkspaceId").GetGuid().Should().Be(workspaceId);
        payload.GetProperty("ConnectorId").GetString().Should().Be("slack");
    }

    [Fact]
    public async Task HandleCallbackAsync_ValidState_StoresCredentialAndReturnsId()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var state = Guid.NewGuid().ToString("N");
        var statePayload = JsonSerializer.Serialize(new { WorkspaceId = workspaceId, ConnectorId = "slack" });
        var expectedCredentialId = Guid.NewGuid();

        var (multiplexerMock, dbMock) = BuildRedisMocks();
        dbMock.Setup(d => d.StringGetDeleteAsync(
                It.Is<RedisKey>(k => k.ToString() == $"oauth:state:{state}"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)statePayload);

        var vaultMock = new Mock<ICredentialVaultService>();
        vaultMock.Setup(v => v.StoreAsync(
                workspaceId,
                "slack",
                It.IsAny<string>(),
                "OAuth2AuthCode",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCredentialId);

        var service = new OAuthService(multiplexerMock.Object, vaultMock.Object, BuildConfig(), NullLogger<OAuthService>.Instance);

        // Act
        var credentialId = await service.HandleCallbackAsync("auth-code-abc", state);

        // Assert
        credentialId.Should().Be(expectedCredentialId);
        vaultMock.Verify(v => v.StoreAsync(
            workspaceId,
            "slack",
            It.IsAny<string>(),
            "OAuth2AuthCode",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallbackAsync_InvalidState_Throws()
    {
        // Arrange
        var (multiplexerMock, dbMock) = BuildRedisMocks();
        dbMock.Setup(d => d.StringGetDeleteAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var vaultMock = new Mock<ICredentialVaultService>();
        var service = new OAuthService(multiplexerMock.Object, vaultMock.Object, BuildConfig(), NullLogger<OAuthService>.Instance);

        // Act
        var act = () => service.HandleCallbackAsync("code", "nonexistent-state");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OAuth state not found or expired*");
    }
}
