using System.Net;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace Flowamaz.Tests.Unit.Connectors.Sandbox;

[Trait("Category", "Sandbox")]
public class ConnectorDispatcherTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef";

    private static (ConnectorDispatcher Dispatcher, List<HttpRequestMessage> Sent, List<string> Bodies) Build(
        string? runnerUrl, Func<HttpResponseMessage>? respond = null, Exception? fail = null,
        IConnectorOperationHandler? local = null)
    {
        var sent = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var http = new Mock<HttpMessageHandler>();
        var setup = http.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        if (fail is not null) setup.ThrowsAsync(fail);
        else
            setup.Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                sent.Add(req);
                bodies.Add(await req.Content!.ReadAsStringAsync());
                return respond!();
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ConnectorDispatcher.HttpClientName)).Returns(() => new HttpClient(http.Object));

        var registry = new Mock<IConnectorOperationHandlerRegistry>();
        registry.Setup(r => r.Get(It.IsAny<string>(), It.IsAny<string>())).Returns(local);

        var settings = new Dictionary<string, string?> { [ConnectorDispatcher.SecretKey] = Secret };
        if (runnerUrl is not null) settings[ConnectorDispatcher.UrlKey] = runnerUrl;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return (new ConnectorDispatcher(registry.Object, factory.Object, config, NullLogger<ConnectorDispatcher>.Instance), sent, bodies);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))) };

    private static Task<JsonDocument> Run(ConnectorDispatcher d, string connector, string op) =>
        d.ExecuteAsync(connector, op, JsonDocument.Parse("{\"url\":\"https://example.org\"}"),
            new ConnectorCredential(Guid.NewGuid(), "c", "ApiKey", "s3cr3t"),
            Guid.NewGuid(), Guid.NewGuid(), "n1", TimeSpan.FromSeconds(30), CancellationToken.None);

    [Fact]
    public async Task WithoutRunnerUrl_RunsLocalHandler()
    {
        var local = new Mock<IConnectorOperationHandler>();
        local.Setup(h => h.ExecuteAsync(It.IsAny<JsonDocument>(), It.IsAny<ConnectorCredential?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonDocument.Parse("{\"local\":true}"));
        var (d, sent, _) = Build(runnerUrl: null, local: local.Object);

        d.SandboxEnabled.Should().BeFalse();
        using var result = await Run(d, "http-rest", "GET");

        result.RootElement.GetProperty("local").GetBoolean().Should().BeTrue();
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SandboxedConnector_IsSignedAndSentToRunner()
    {
        var (d, sent, bodies) = Build("http://runner:8080/",
            () => Json(new ConnectorRunResponse(true, JsonDocument.Parse("{\"status\":200}").RootElement, null, false)));

        d.IsSandboxed("http-rest").Should().BeTrue();
        using var result = await Run(d, "http-rest", "GET");

        result.RootElement.GetProperty("status").GetInt32().Should().Be(200);
        sent.Should().ContainSingle();
        sent[0].RequestUri!.ToString().Should().Be("http://runner:8080/v1/execute");
        var ts = sent[0].Headers.GetValues(RunnerSignature.TimestampHeader).Single();
        var sig = sent[0].Headers.GetValues(RunnerSignature.SignatureHeader).Single();
        RunnerSignature.Verify(Secret, ts, sig, bodies[0], DateTimeOffset.UtcNow).Should().BeTrue();
        bodies[0].Should().Contain("\"connectorId\":\"http-rest\"").And.Contain("\"operationId\":\"GET\"");
    }

    [Fact]
    public void NonSandboxedConnector_StaysInProcess()
    {
        var (d, _, _) = Build("http://runner:8080");
        d.IsSandboxed("email").Should().BeFalse();
        d.IsSandboxed("slack").Should().BeTrue();
    }

    [Fact]
    public async Task RunnerFailure_PreservesRetryableFlag()
    {
        var (d, _, _) = Build("http://runner:8080", () => Json(new ConnectorRunResponse(false, null, "Blocked call to 'x'", false)));

        var act = () => Run(d, "http-rest", "GET");

        (await act.Should().ThrowAsync<ConnectorOperationException>())
            .Which.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task Unauthorized_IsFatalWithSecretHint()
    {
        var (d, _, _) = Build("http://runner:8080", () => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = (await FluentActions.Awaiting(() => Run(d, "http-rest", "GET"))
            .Should().ThrowAsync<ConnectorOperationException>()).Which;

        ex.Retryable.Should().BeFalse();
        ex.Message.Should().Contain("CONNECTOR_RUNNER_SECRET");
    }

    [Fact]
    public async Task RunnerUnreachable_IsRetryable()
    {
        var (d, _, _) = Build("http://runner:8080", fail: new HttpRequestException("connection refused"));

        (await FluentActions.Awaiting(() => Run(d, "http-rest", "GET"))
            .Should().ThrowAsync<ConnectorOperationException>()).Which.Retryable.Should().BeTrue();
    }

    [Fact]
    public void CredentialSecret_IsNotInToString()
    {
        var cred = new ConnectorRunCredential(Guid.NewGuid(), "slack", "OAuth2", "xoxb-super-secret");
        cred.ToString().Should().NotContain("xoxb-super-secret");
    }
}
