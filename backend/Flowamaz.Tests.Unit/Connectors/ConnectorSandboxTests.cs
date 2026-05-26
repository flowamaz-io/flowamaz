using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Connectors;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Connectors;

/// <summary>
/// Unit tests for <see cref="ConnectorSandbox"/> HTTP execution, schema validation, and error handling.
/// </summary>
[Trait("Category", "Connectors")]
public class ConnectorSandboxTests
{
    private static readonly string ValidManifest = """
        {
          "operations": [
            {
              "id": "get-user",
              "http": { "method": "GET", "url": "https://api.example.com/user" },
              "input_schema": { "type": "object", "properties": { "userId": { "type": "string" } }, "required": ["userId"] },
              "output_schema": { "type": "object", "properties": { "id": { "type": "string" } } }
            }
          ]
        }
        """;

    private static FlowAmazDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"sandbox-{Guid.NewGuid():N}")
            .Options);

    private static ConnectorDefinition MakeDefinition(string connectorId, string manifestJson) => new()
    {
        Id = Guid.NewGuid(),
        ConnectorId = connectorId,
        PublisherId = "test",
        DisplayName = connectorId,
        Version = "1.0.0",
        Category = "test",
        Tags = [],
        ManifestJson = manifestJson,
        Tier = ConnectorTier.Official,
        IsEnabled = true,
        IsInstalled = false,
        WorkspaceId = null
    };

    private static ConnectorSandbox NewSandbox(
        FlowAmazDbContext db,
        ICredentialVaultService? vault = null,
        IHttpClientFactory? httpFactory = null) =>
        new(db,
            vault ?? Mock.Of<ICredentialVaultService>(),
            httpFactory ?? Mock.Of<IHttpClientFactory>(),
            NullLogger<ConnectorSandbox>.Instance);

    private static IHttpClientFactory FakeHttpFactory(HttpStatusCode status, string responseBody)
    {
        var handler = new FakeHttpMessageHandler(status, responseBody);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ConnectorSandbox.HttpClientName)).Returns(client);
        return factory.Object;
    }

    private static IHttpClientFactory ThrowingHttpFactory(Exception ex)
    {
        var handler = new ThrowingHttpMessageHandler(ex);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ConnectorSandbox.HttpClientName)).Returns(client);
        return factory.Object;
    }

    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidInput_BuildsCorrectHttpRequest()
    {
        // Arrange
        var db = NewDb();
        var def = MakeDefinition("my-api", ValidManifest);
        db.ConnectorDefinitions.Add(def);
        await db.SaveChangesAsync();

        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, """{"id":"u1"}""");
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ConnectorSandbox.HttpClientName)).Returns(client);

        var sandbox = NewSandbox(db, httpFactory: factory.Object);
        var input = JsonDocument.Parse("""{"userId":"abc"}""");

        // Act
        var result = await sandbox.ExecuteAsync(Guid.NewGuid(), "my-api", "get-user", input, Guid.Empty);

        // Assert
        result.Success.Should().BeTrue();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://api.example.com/user");
    }

    [Fact]
    public async Task ExecuteAsync_InputFailsSchemaValidation_ReturnsErrorBeforeHttpCall()
    {
        // Arrange
        var db = NewDb();
        var def = MakeDefinition("my-api", ValidManifest);
        db.ConnectorDefinitions.Add(def);
        await db.SaveChangesAsync();

        var httpCallMade = false;
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, "{}");
        handler.OnSend = () => httpCallMade = true;
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ConnectorSandbox.HttpClientName)).Returns(client);

        var sandbox = NewSandbox(db, httpFactory: factory.Object);
        // Missing required 'userId' field
        var input = JsonDocument.Parse("""{}""");

        // Act
        var result = await sandbox.ExecuteAsync(Guid.NewGuid(), "my-api", "get-user", input, Guid.Empty);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("validation failed");
        httpCallMade.Should().BeFalse("HTTP call must not happen when input is invalid");
    }

    [Fact]
    public async Task ExecuteAsync_HttpReturns500_ReturnsErrorResult()
    {
        // Arrange
        var db = NewDb();
        var def = MakeDefinition("my-api", ValidManifest);
        db.ConnectorDefinitions.Add(def);
        await db.SaveChangesAsync();

        var sandbox = NewSandbox(db, httpFactory: FakeHttpFactory(HttpStatusCode.InternalServerError, "server error"));
        var input = JsonDocument.Parse("""{"userId":"abc"}""");

        // Act
        var result = await sandbox.ExecuteAsync(Guid.NewGuid(), "my-api", "get-user", input, Guid.Empty);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task ExecuteAsync_HttpReturns200_ReturnsTypedOutput()
    {
        // Arrange
        var db = NewDb();
        var def = MakeDefinition("my-api", ValidManifest);
        db.ConnectorDefinitions.Add(def);
        await db.SaveChangesAsync();

        var sandbox = NewSandbox(db, httpFactory: FakeHttpFactory(HttpStatusCode.OK, """{"id":"user-123"}"""));
        var input = JsonDocument.Parse("""{"userId":"user-123"}""");

        // Act
        var result = await sandbox.ExecuteAsync(Guid.NewGuid(), "my-api", "get-user", input, Guid.Empty);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().NotBeNull();
        result.Output!.RootElement.GetProperty("id").GetString().Should().Be("user-123");
    }

    [Fact]
    public async Task ExecuteAsync_NetworkTimeout_ReturnsTimeoutError()
    {
        // Arrange
        var db = NewDb();
        var def = MakeDefinition("my-api", ValidManifest);
        db.ConnectorDefinitions.Add(def);
        await db.SaveChangesAsync();

        var sandbox = NewSandbox(db, httpFactory: ThrowingHttpFactory(new TaskCanceledException("timeout")));
        var input = JsonDocument.Parse("""{"userId":"abc"}""");

        // Act
        var result = await sandbox.ExecuteAsync(Guid.NewGuid(), "my-api", "get-user", input, Guid.Empty);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // ─── HTTP helpers ─────────────────────────────────────────────────────────

    private sealed class CapturingHttpMessageHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public Action? OnSend { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            OnSend?.Invoke();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ThrowingHttpMessageHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(ex);
    }
}
