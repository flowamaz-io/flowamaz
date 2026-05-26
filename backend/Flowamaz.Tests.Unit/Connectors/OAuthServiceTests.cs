using System.Net;
using System.Text;
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
    private static IConfiguration BuildConfig(Dictionary<string, string?>? extra = null)
    {
        var defaults = new Dictionary<string, string?>
        {
            ["Platform:BaseUrl"] = "https://flowamaz.io",
            ["Connectors:Slack:ClientId"] = "test-slack-client-id",
            ["SLACK_CLIENT_ID"] = "test-slack-client-id",
            ["SLACK_CLIENT_SECRET"] = "test-slack-client-secret",
            ["GITHUB_CLIENT_ID"] = "test-github-client-id",
            ["GITHUB_CLIENT_SECRET"] = "test-github-client-secret",
            ["MICROSOFT_CLIENT_ID"] = "test-ms-client-id",
            ["MICROSOFT_CLIENT_SECRET"] = "test-ms-client-secret",
            ["Connectors:GitHub:ClientId"] = "test-github-client-id",
            ["Connectors:MicrosoftTeams:ClientId"] = "test-teams-client-id"
        };
        if (extra is not null)
            foreach (var (k, v) in extra) defaults[k] = v;

        return new ConfigurationBuilder().AddInMemoryCollection(defaults).Build();
    }

    private static (Mock<IConnectionMultiplexer> MultiplexerMock, Mock<IDatabase> DbMock) BuildRedisMocks()
    {
        var dbMock = new Mock<IDatabase>();
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
        return (multiplexerMock, dbMock);
    }

    private static IHttpClientFactory FakeHttp(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(status, body);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OAuthService.HttpClientName)).Returns(client);
        return factory.Object;
    }

    private static OAuthService BuildService(
        Mock<IConnectionMultiplexer> redis,
        Mock<ICredentialVaultService> vault,
        IHttpClientFactory? http = null,
        IConfiguration? config = null)
    {
        http ??= Mock.Of<IHttpClientFactory>();
        config ??= BuildConfig();
        return new OAuthService(redis.Object, vault.Object, http, config, NullLogger<OAuthService>.Instance);
    }

    // ─── Existing tests (updated for new constructor) ─────────────────────────

    [Fact]
    public async Task InitiateAsync_StoresStateInRedis_AndReturnsAuthorizationUrl()
    {
        var (multiplexerMock, dbMock) = BuildRedisMocks();
        string? storedKey = null;
        string? storedValue = null;

        dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((key, value, _, _, _, _) =>
            {
                storedKey = key.ToString();
                storedValue = value.ToString();
            })
            .ReturnsAsync(true);

        var vaultMock = new Mock<ICredentialVaultService>();
        var service = BuildService(multiplexerMock, vaultMock);
        var workspaceId = Guid.NewGuid();

        var result = await service.InitiateAsync(workspaceId, "slack");

        result.AuthorizationUrl.Should().Contain("slack.com/oauth/v2/authorize");
        result.AuthorizationUrl.Should().Contain("test-slack-client-id");
        result.State.Should().NotBeNullOrEmpty();

        storedKey.Should().Be($"oauth:state:{result.State}");
        var payload = JsonDocument.Parse(storedValue!).RootElement;
        payload.GetProperty("WorkspaceId").GetGuid().Should().Be(workspaceId);
        payload.GetProperty("ConnectorId").GetString().Should().Be("slack");
    }

    [Fact]
    public async Task HandleCallbackAsync_InvalidState_Throws()
    {
        var (multiplexerMock, dbMock) = BuildRedisMocks();
        dbMock.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var vaultMock = new Mock<ICredentialVaultService>();
        var service = BuildService(multiplexerMock, vaultMock);

        var act = () => service.HandleCallbackAsync("code", "nonexistent-state");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OAuth state not found or expired*");
    }

    // ─── New tests for real token exchange ────────────────────────────────────

    [Fact]
    public async Task ExchangeCode_Slack_SendsCorrectFormParams()
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

        // Mock Slack's OAuth endpoint returning success
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"ok":true,"access_token":"xoxb-test","bot_user_id":"U123"}""");
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OAuthService.HttpClientName)).Returns(client);

        var vaultMock = new Mock<ICredentialVaultService>();
        vaultMock.Setup(v => v.StoreAsync(workspaceId, "slack", It.IsAny<string>(),
                "OAuth2AuthCode", "xoxb-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCredentialId);

        var service = BuildService(multiplexerMock, vaultMock, factory.Object);

        // Act
        var result = await service.HandleCallbackAsync("auth-code-slack", state);

        // Assert
        result.Should().Be(expectedCredentialId);
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("slack.com/api/oauth.v2.access");

        // Verify form params were sent
        var formBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        formBody.Should().Contain("code=auth-code-slack");
        formBody.Should().Contain("client_id=test-slack-client-id");
        formBody.Should().Contain("client_secret=test-slack-client-secret");
    }

    [Fact]
    public async Task ExchangeCode_Slack_InvalidCode_ThrowsOAuthException()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var state = Guid.NewGuid().ToString("N");
        var statePayload = JsonSerializer.Serialize(new { WorkspaceId = workspaceId, ConnectorId = "slack" });

        var (multiplexerMock, dbMock) = BuildRedisMocks();
        dbMock.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)statePayload);

        // Slack returns ok:false with error code
        var httpFactory = FakeHttp(HttpStatusCode.OK, """{"ok":false,"error":"invalid_code"}""");

        var vaultMock = new Mock<ICredentialVaultService>();
        var service = BuildService(multiplexerMock, vaultMock, httpFactory);

        // Act
        var act = () => service.HandleCallbackAsync("bad-code", state);

        // Assert
        await act.Should().ThrowAsync<OAuthException>()
            .WithMessage("*Slack token exchange failed*invalid_code*");
    }

    [Fact]
    public async Task ExchangeCode_GitHub_ParsesJsonResponse()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var state = Guid.NewGuid().ToString("N");
        var statePayload = JsonSerializer.Serialize(new { WorkspaceId = workspaceId, ConnectorId = "github" });
        var expectedCredentialId = Guid.NewGuid();

        var (multiplexerMock, dbMock) = BuildRedisMocks();
        dbMock.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)statePayload);

        var httpFactory = FakeHttp(HttpStatusCode.OK, """{"access_token":"gho-test-token","scope":"repo","token_type":"bearer"}""");

        var vaultMock = new Mock<ICredentialVaultService>();
        vaultMock.Setup(v => v.StoreAsync(workspaceId, "github", It.IsAny<string>(),
                "OAuth2AuthCode", "gho-test-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCredentialId);

        var service = BuildService(multiplexerMock, vaultMock, httpFactory);

        // Act
        var result = await service.HandleCallbackAsync("github-code", state);

        // Assert
        result.Should().Be(expectedCredentialId);
    }

    [Fact]
    public async Task ExchangeCode_Microsoft_ParsesJsonResponse_WithAccessToken()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var state = Guid.NewGuid().ToString("N");
        var statePayload = JsonSerializer.Serialize(new { WorkspaceId = workspaceId, ConnectorId = "microsoft-teams" });
        var expectedCredentialId = Guid.NewGuid();

        var (multiplexerMock, dbMock) = BuildRedisMocks();
        dbMock.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)statePayload);

        var msResponse = """{"access_token":"eyJ-ms-token","refresh_token":"ref-token","expires_in":3600,"token_type":"Bearer"}""";
        var httpFactory = FakeHttp(HttpStatusCode.OK, msResponse);

        var vaultMock = new Mock<ICredentialVaultService>();
        vaultMock.Setup(v => v.StoreAsync(workspaceId, "microsoft-teams", It.IsAny<string>(),
                "OAuth2AuthCode", "eyJ-ms-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCredentialId);

        var service = BuildService(multiplexerMock, vaultMock, httpFactory);

        // Act
        var result = await service.HandleCallbackAsync("ms-code", state);

        // Assert
        result.Should().Be(expectedCredentialId);
    }

    [Fact]
    public async Task ExchangeCode_UnknownProvider_ThrowsNotSupportedException()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var state = Guid.NewGuid().ToString("N");
        var statePayload = JsonSerializer.Serialize(new { WorkspaceId = workspaceId, ConnectorId = "unknown-provider" });

        var (multiplexerMock, dbMock) = BuildRedisMocks();
        dbMock.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)statePayload);

        var vaultMock = new Mock<ICredentialVaultService>();
        var service = BuildService(multiplexerMock, vaultMock);

        // Act
        var act = () => service.HandleCallbackAsync("code", state);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*unknown-provider*");
    }

    // ─── HTTP helpers ─────────────────────────────────────────────────────────

    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class CapturingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
