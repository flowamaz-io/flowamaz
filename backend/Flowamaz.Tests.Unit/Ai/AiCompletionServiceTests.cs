using System.Net;
using System.Text;
using FluentAssertions;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Flowamaz.Tests.Unit.Ai;

/// <summary>
/// AiCompletionService (fix-02-02): with a resolved platform key it POSTs to the Anthropic Messages
/// API over the injected HttpClient and returns the provider's text + token usage; with no key it
/// falls back to a deterministic local stub and logs a warning. Provider errors propagate so the
/// caller decides. The HTTP transport is mocked — no network call is made.
/// </summary>
public class AiCompletionServiceTests
{
    private static ModelConfig Config(string? apiKey) =>
        new("claude-haiku-4-5", "anthropic", AiKeySource.Platform, apiKey, false, 200_000, true, true);

    private static IOptions<AiOptions> Options(bool useStub = false) =>
        Microsoft.Extensions.Options.Options.Create(new AiOptions { UseStubCompletion = useStub });

    private static (Mock<IHttpClientFactory> factory, RecordingHandler handler) FactoryReturning(
        HttpStatusCode status, string body)
    {
        var handler = new RecordingHandler(status, body);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AiCompletionService.HttpClientName)).Returns(new HttpClient(handler));
        return (factory, handler);
    }

    [Fact]
    public async Task NoPlatformKey_ReturnsStubAndLogsWarning()
    {
        var factory = new Mock<IHttpClientFactory>();
        var logger = new Mock<ILogger<AiCompletionService>>();
        var service = new AiCompletionService(factory.Object, Options(), logger.Object);

        var result = await service.CompleteAsync(Config(apiKey: ""), "system", "Order total is 47500.");

        result.Text.Should().Contain("47500"); // stub echoes the prompt facts
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never); // no provider call
        logger.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task PlatformKeyConfigured_CallsAnthropicMessagesEndpoint()
    {
        const string body = """
            { "content": [ { "type": "text", "text": "Hello from Claude" } ],
              "usage": { "input_tokens": 11, "output_tokens": 7 } }
            """;
        var (factory, handler) = FactoryReturning(HttpStatusCode.OK, body);
        var service = new AiCompletionService(factory.Object, Options(), NullLogger());

        var result = await service.CompleteAsync(Config("sk-ant-test"), "system", "summarise this run");

        result.Text.Should().Be("Hello from Claude");
        result.TokensInput.Should().Be(11);
        result.TokensOutput.Should().Be(7);
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        handler.LastRequest.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("sk-ant-test");
        handler.LastRequest.Headers.GetValues("anthropic-version").Should().ContainSingle();
    }

    [Fact]
    public async Task ProviderReturnsError_PropagatesException()
    {
        var (factory, _) = FactoryReturning(HttpStatusCode.InternalServerError, "{\"error\":\"overloaded\"}");
        var service = new AiCompletionService(factory.Object, Options(), NullLogger());

        var act = async () => await service.CompleteAsync(Config("sk-ant-test"), "system", "user");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task UseStubCompletion_ForcesStubEvenWithKey()
    {
        var factory = new Mock<IHttpClientFactory>();
        var service = new AiCompletionService(factory.Object, Options(useStub: true), NullLogger());

        var result = await service.CompleteAsync(Config("sk-ant-test"), "system", "Order total is 47500.");

        result.Text.Should().Contain("47500");
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never); // no provider call despite key
    }

    private static ILogger<AiCompletionService> NullLogger() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AiCompletionService>.Instance;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public RecordingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Buffer the content so headers/body remain readable after the handler returns.
            if (request.Content is not null) await request.Content.LoadIntoBufferAsync();
            LastRequest = request;
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
