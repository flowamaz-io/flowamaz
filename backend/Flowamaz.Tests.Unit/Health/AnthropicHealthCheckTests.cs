using System.Net;
using Flowamaz.Application.Health;
using Flowamaz.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Flowamaz.Tests.Unit.Health;

/// <summary>
/// O6: /health used to report anthropic "healthy" while the platform key was rejected. The probe now
/// validates the key with the free models call, caches the result, and reports Degraded with a detail.
/// </summary>
public sealed class AnthropicHealthCheckTests
{
    private sealed class CountingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Last = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static AnthropicHealthCheck Create(HttpMessageHandler handler, AiOptions options, string environment = "Development")
    {
        AnthropicHealthCheck.ResetCache();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AnthropicHealthCheck.HttpClientName)).Returns(() => new HttpClient(handler, disposeHandler: false));
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        return new AnthropicHealthCheck(factory.Object, env.Object, Options.Create(options), NullLogger<AnthropicHealthCheck>.Instance);
    }

    [Fact]
    public async Task Rejected_platform_key_reports_degraded_with_actionable_detail_and_is_cached()
    {
        var handler = new CountingHandler(HttpStatusCode.Unauthorized);
        var check = Create(handler, new AiOptions { AnthropicPlatformKey = "sk-ant-bad" });

        var first = await check.CheckHealthAsync(new HealthCheckContext());
        var second = await check.CheckHealthAsync(new HealthCheckContext());

        first.Status.Should().Be(HealthStatus.Degraded);
        first.Description.Should().Contain("rejected").And.Contain("Ai__AnthropicPlatformKey").And.NotContain("sk-ant-bad");
        second.Status.Should().Be(HealthStatus.Degraded);
        handler.Calls.Should().Be(1, "the validation result is cached so /health never hammers the provider");
        handler.Last!.Method.Should().Be(HttpMethod.Get);
        handler.Last.RequestUri!.AbsolutePath.Should().Be("/v1/models");
    }

    [Fact]
    public async Task Accepted_platform_key_reports_healthy()
    {
        var check = Create(new CountingHandler(HttpStatusCode.OK), new AiOptions { AnthropicPlatformKey = "sk-ant-good" });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("accepted");
    }

    [Fact]
    public async Task Missing_key_is_degraded_unless_stub_is_allowed_and_no_call_is_made()
    {
        var handler = new CountingHandler(HttpStatusCode.OK);

        (await Create(handler, new AiOptions()).CheckHealthAsync(new HealthCheckContext()))
            .Status.Should().Be(HealthStatus.Degraded);
        var stub = await Create(handler, new AiOptions { AllowStubWithoutKey = true }).CheckHealthAsync(new HealthCheckContext());
        stub.Status.Should().Be(HealthStatus.Healthy);
        stub.Description.Should().Contain("stub");
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Test_environment_never_calls_the_provider()
    {
        var handler = new CountingHandler(HttpStatusCode.Unauthorized);
        var result = await Create(handler, new AiOptions { AnthropicPlatformKey = "k" }, environment: "Test")
            .CheckHealthAsync(new HealthCheckContext());

        result.Description.Should().StartWith("Not checked");
        handler.Calls.Should().Be(0);
    }
}
