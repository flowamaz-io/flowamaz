using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Flowamaz.EgressProxy;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Connectors.Sandbox;

[Trait("Category", "Sandbox")]
public class EgressProxyServerTests
{
    private static async Task<string> SendRawAsync(int proxyPort, string request, int readBytes = 4096)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        var buffer = new byte[readBytes];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var read = await stream.ReadAsync(buffer, cts.Token);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    private static EgressProxyServer Start(EgressProxyOptions options)
    {
        var server = new EgressProxyServer(options with { Port = 0 }, NullLogger.Instance);
        server.Start(IPAddress.Loopback);
        return server;
    }

    [Theory]
    [InlineData("CONNECT 127.0.0.1:443 HTTP/1.1\r\nHost: 127.0.0.1:443\r\n\r\n")]
    [InlineData("CONNECT 169.254.169.254:80 HTTP/1.1\r\nHost: x\r\n\r\n")]
    [InlineData("GET http://10.0.0.5/admin HTTP/1.1\r\nHost: 10.0.0.5\r\n\r\n")]
    [InlineData("CONNECT localhost:443 HTTP/1.1\r\nHost: localhost\r\n\r\n")]
    public async Task InternalDestinations_Are403(string request)
    {
        await using var server = Start(new EgressProxyOptions());
        var response = await SendRawAsync(server.Port, request);
        response.Should().StartWith("HTTP/1.1 403").And.Contain("private or internal network");
    }

    [Fact]
    public async Task DisallowedPort_Is403()
    {
        await using var server = Start(new EgressProxyOptions());
        var response = await SendRawAsync(server.Port, "CONNECT 8.8.8.8:22 HTTP/1.1\r\nHost: x\r\n\r\n");
        response.Should().StartWith("HTTP/1.1 403").And.Contain("port 22");
    }

    [Fact]
    public async Task OriginFormRequest_Is400()
    {
        await using var server = Start(new EgressProxyOptions());
        var response = await SendRawAsync(server.Port, "GET /index.html HTTP/1.1\r\nHost: x\r\n\r\n");
        response.Should().StartWith("HTTP/1.1 400");
    }

    [Fact]
    public async Task AllowedDestination_TunnelsBytesBothWays()
    {
        // A local echo server stands in for the internet (private networks allowed only in this test).
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        var echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;
        var echoTask = Task.Run(async () =>
        {
            using var c = await echo.AcceptTcpClientAsync();
            var s = c.GetStream();
            var buf = new byte[64];
            var n = await s.ReadAsync(buf);
            await s.WriteAsync(buf.AsMemory(0, n));
        });

        await using var server = Start(new EgressProxyOptions { AllowPrivateNetworks = true, AllowedPorts = new HashSet<int> { echoPort } });
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{echoPort} HTTP/1.1\r\nHost: x\r\n\r\n"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[256];
        var read = await stream.ReadAsync(buffer, cts.Token);
        Encoding.ASCII.GetString(buffer, 0, read).Should().StartWith("HTTP/1.1 200");

        await stream.WriteAsync("ping"u8.ToArray(), cts.Token);
        read = await stream.ReadAsync(buffer, cts.Token);
        Encoding.ASCII.GetString(buffer, 0, read).Should().Be("ping");

        await echoTask;
        echo.Stop();
    }

    [Fact]
    public void ParsePorts_IgnoresInvalidEntries() =>
        EgressProxyProgram.ParsePorts("443, 80, abc, 70000, -1").Should().BeEquivalentTo([443, 80]);
}
