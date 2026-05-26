using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Connectors;

/// <summary>
/// Unit tests for <see cref="ByomProviderService"/>.
/// Verifies OpenAI-compatible request construction, auth header, and response parsing.
/// </summary>
[Trait("Category", "Connectors")]
public class ByomProviderServiceTests
{
    private const string ByomUrl = "https://my-byom-endpoint.example.com";
    private const string ByomApiKey = "byom-api-key-test-value";
    private const string ByomCredentialJson = $$$"""{"url":"{{{ByomUrl}}}","api_key":"{{{ByomApiKey}}}"}""";

    private static (ByomProviderService service, CapturingHandler handler) Build(
        HttpStatusCode statusCode, string responseBody)
    {
        var handler = new CapturingHandler(statusCode, responseBody);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ByomProviderService.HttpClientName)).Returns(client);

        var vaultMock = new Mock<ICredentialVaultService>();
        vaultMock.Setup(v => v.RetrieveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ByomCredentialJson);

        var service = new ByomProviderService(
            vaultMock.Object,
            factory.Object,
            NullLogger<ByomProviderService>.Instance);

        return (service, handler);
    }

    [Fact]
    public async Task CompleteAsync_ValidByomEndpoint_SendsOpenAiCompatibleRequest()
    {
        // Arrange
        var responseJson = """
            {
              "choices": [
                { "message": { "role": "assistant", "content": "Hello from BYOM!" } }
              ]
            }
            """;
        var (service, handler) = Build(HttpStatusCode.OK, responseJson);

        // Act
        var result = await service.CompleteAsync(
            Guid.NewGuid(), Guid.NewGuid(), "my-model", "Say hello", 100);

        // Assert
        result.Should().Be("Hello from BYOM!");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().Be($"{ByomUrl}/v1/chat/completions");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.CapturedAuthHeader.Should().Contain("Bearer").And.Contain(ByomApiKey);

        handler.CapturedBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("my-model");
        doc.RootElement.GetProperty("messages").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CompleteAsync_ByomReturns401_ThrowsHttpRequestException()
    {
        // Arrange
        var (service, _) = Build(HttpStatusCode.Unauthorized, """{"error":"invalid_token"}""");

        // Act
        var act = async () => await service.CompleteAsync(
            Guid.NewGuid(), Guid.NewGuid(), "my-model", "Hello", 100);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*401*");
    }

    [Fact]
    public async Task CompleteAsync_ResponseParsed_ReturnsContentText()
    {
        // Arrange
        var expectedContent = "This is the model's answer.";
        var responseJson = $$"""
            {
              "choices": [
                { "message": { "role": "assistant", "content": "{{expectedContent}}" } }
              ]
            }
            """;
        var (service, _) = Build(HttpStatusCode.OK, responseJson);

        // Act
        var result = await service.CompleteAsync(
            Guid.NewGuid(), Guid.NewGuid(), "gpt-4o", "Question?", 200);

        // Assert
        result.Should().Be(expectedContent);
    }

    // ─── HTTP helper ─────────────────────────────────────────────────────────

    internal sealed class CapturingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? CapturedBody { get; private set; }
        public string? CapturedAuthHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            // Read content before the caller disposes the request.
            if (request.Content is not null)
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            CapturedAuthHeader = request.Headers.Authorization?.ToString();

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
