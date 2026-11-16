using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Interfaces.Runtime;
using Flowamaz.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Engine.Runtime.Tests.Hosting;

/// <summary>Anthropic with the host's own key (no real calls: a fake HTTP handler answers).</summary>
public class AnthropicModelProviderTests
{
    // Not a real key: tests check it is sent as the header and never anywhere else.
    private const string TestKey = "test-key-not-real";

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (AnthropicModelProvider Provider, FakeHandler Handler) Provider(
        HttpStatusCode status, string body, Dictionary<string, string?>? settings = null)
    {
        var handler = new FakeHandler(status, body);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AnthropicModelProvider.HttpClientName)).Returns(() => new HttpClient(handler, disposeHandler: false));
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>
        {
            ["Ai:Anthropic:ApiKey"] = TestKey,
            ["Ai:Anthropic:Model"] = "model-a",
            ["Ai:Pricing:model-a:InputUsdPerMillionTokens"] = "1.00",
            ["Ai:Pricing:model-a:OutputUsdPerMillionTokens"] = "5.00",
        }).Build();
        return (new AnthropicModelProvider(factory.Object, config, NullLogger<AnthropicModelProvider>.Instance), handler);
    }

    private static AiCompletionRequest Request(string? system = "Answer briefly") =>
        new(Guid.NewGuid(), "model-a", system, "Is 4200 a small loan?", 256);

    private const string Reply = """
        {"model":"model-a","content":[{"type":"text","text":"Yes, "},{"type":"text","text":"it is."}],
         "usage":{"input_tokens":2000,"output_tokens":400}}
        """;

    [Fact]
    public async Task Sends_the_messages_request_with_the_key_and_reads_text_tokens_and_cost()
    {
        var (provider, handler) = Provider(HttpStatusCode.OK, Reply);

        var completion = await provider.CompleteAsync(Request());

        completion.Should().Be(new AiCompletion("Yes, it is.", "model-a", 2000, 400, 0.004m)); // 2000×$1 + 400×$5 per M
        handler.Request!.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        handler.Request.Headers.GetValues("x-api-key").Should().Equal(TestKey);
        handler.Request.Headers.GetValues("anthropic-version").Should().ContainSingle();
        using var sent = JsonDocument.Parse(handler.RequestBody!);
        sent.RootElement.GetProperty("model").GetString().Should().Be("model-a");
        sent.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(256);
        sent.RootElement.GetProperty("system").GetString().Should().Be("Answer briefly");
        sent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be("Is 4200 a small loan?");
        provider.DefaultModel.Should().Be("model-a");
    }

    [Fact]
    public async Task No_system_prompt_is_left_out_and_no_prices_mean_no_cost()
    {
        var (provider, handler) = Provider(HttpStatusCode.OK, Reply,
            new Dictionary<string, string?> { ["Ai:Anthropic:ApiKey"] = TestKey, ["Ai:Anthropic:BaseUrl"] = "https://proxy.example.com/" });

        var completion = await provider.CompleteAsync(Request(system: null));

        completion.CostUsd.Should().BeNull();
        handler.RequestBody.Should().NotContain("\"system\"");
        handler.Request!.RequestUri!.ToString().Should().Be("https://proxy.example.com/v1/messages");
    }

    [Fact]
    public async Task Without_a_key_it_says_which_setting_to_set_and_calls_nothing()
    {
        var (provider, handler) = Provider(HttpStatusCode.OK, Reply, new Dictionary<string, string?>());

        var act = () => provider.CompleteAsync(Request());

        var ex = (await act.Should().ThrowAsync<AiProviderException>()).Which;
        ex.Retryable.Should().BeFalse();
        ex.Message.Should().Contain("Ai:Anthropic:ApiKey");
        handler.Request.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData((HttpStatusCode)529, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task Errors_are_retryable_only_when_transient_and_never_echo_the_key(HttpStatusCode status, bool retryable)
    {
        var (provider, _) = Provider(status, """{"type":"error","error":{"type":"x","message":"something went wrong"}}""");

        var act = () => provider.CompleteAsync(Request());

        var ex = (await act.Should().ThrowAsync<AiProviderException>()).Which;
        ex.Retryable.Should().Be(retryable);
        ex.Message.Should().Contain($"HTTP {(int)status}").And.Contain("something went wrong").And.NotContain(TestKey);
    }
}
