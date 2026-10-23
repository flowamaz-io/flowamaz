using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.ConnectorRunner;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Flowamaz.Tests.Unit.Connectors.Sandbox;

/// <summary>The runner's /v1/execute endpoint, hosted in-memory (no network access needed).</summary>
[Trait("Category", "Sandbox")]
public class ConnectorRunnerEndpointTests : IClassFixture<ConnectorRunnerEndpointTests.RunnerFactory>
{
    private const string Secret = "0123456789abcdef0123456789abcdef";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly RunnerFactory _factory;

    public ConnectorRunnerEndpointTests(RunnerFactory factory) => _factory = factory;

    public sealed class RunnerFactory : WebApplicationFactory<ConnectorRunnerEntryPoint>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Runner:SharedSecret", Secret);
            builder.UseSetting("Runner:EgressProxy", "");
        }
    }

    private static HttpRequestMessage Signed(object request, string secret = Secret, long? ts = null)
    {
        var body = JsonSerializer.Serialize(request, Json);
        var stamp = ts ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var msg = new HttpRequestMessage(HttpMethod.Post, "/v1/execute") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        msg.Headers.Add(RunnerSignature.TimestampHeader, stamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        msg.Headers.Add(RunnerSignature.SignatureHeader, RunnerSignature.Compute(secret, stamp, body));
        return msg;
    }

    private static ConnectorRunRequest Request(string connector, string op, string input = "{}") =>
        new(connector, op, JsonDocument.Parse(input).RootElement, null, Guid.NewGuid(), Guid.NewGuid(), "n1", 10, null);

    [Fact]
    public async Task Health_IsOk()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnsignedOrWronglySigned_Is401()
    {
        var client = _factory.CreateClient();
        var unsigned = await client.PostAsJsonAsync("/v1/execute", Request("http-rest", "GET"), Json);
        var wrong = await client.SendAsync(Signed(Request("http-rest", "GET"), secret: Secret + "-other"));
        var stale = await client.SendAsync(Signed(Request("http-rest", "GET"), ts: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds()));

        unsigned.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        stale.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NonSandboxedConnector_IsRefused()
    {
        var response = await _factory.CreateClient().SendAsync(Signed(Request("postgresql", "query")));
        var result = await response.Content.ReadFromJsonAsync<ConnectorRunResponse>(Json);

        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("not run in the sandbox");
    }

    [Fact]
    public async Task PrivateDestination_IsBlockedInsideTheRunner()
    {
        // No proxy configured here → the in-process IP guard is the last line; metadata IP is refused.
        var response = await _factory.CreateClient().SendAsync(
            Signed(Request("http-rest", "GET", "{\"url\":\"http://169.254.169.254/latest/meta-data/\"}")));
        var result = await response.Content.ReadFromJsonAsync<ConnectorRunResponse>(Json);

        result!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DomainAllowlist_RefusesHostsOutsideTheList()
    {
        RunnerCallScope.AllowedDomains = ["slack.com"];
        try
        {
            using var client = new HttpClient(new DomainAllowlistHandler { InnerHandler = new HttpClientHandler() });
            var act = () => client.GetAsync("https://attacker.example/steal");

            (await act.Should().ThrowAsync<ConnectorOperationException>()).Which.Message.Should().Contain("slack.com");
        }
        finally
        {
            RunnerCallScope.AllowedDomains = null;
        }
    }
}
