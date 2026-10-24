using System.Net;
using System.Net.Sockets;
using System.Text;
using Flowamaz.Core.Security;
using Microsoft.Extensions.Logging;

namespace Flowamaz.EgressProxy;

public sealed record EgressProxyOptions
{
    public int Port { get; init; } = 3128;
    /// <summary>Destination ports the sandbox may reach (default 443 and 80).</summary>
    public IReadOnlySet<int> AllowedPorts { get; init; } = new HashSet<int> { 443, 80 };
    /// <summary>Tests/self-hosted only: allow private destinations.</summary>
    public bool AllowPrivateNetworks { get; init; }
    public int MaxConnections { get; init; } = 512;
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Minimal forward proxy for the connector sandbox (FUNCTIONAL.md §9.5 "egress proxy enforces the
/// network policy"). Supports CONNECT tunnels (HTTPS) and absolute-URI plain HTTP. Every destination
/// is resolved here and refused when any candidate address is internal (loopback, RFC1918,
/// link-local/cloud metadata, CGNAT, ULA …) or the port is not allowed — the check is on the resolved
/// IP, so DNS tricks cannot reach internal services. Decisions are logged without payloads.
/// </summary>
public sealed class EgressProxyServer : IAsyncDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;

    private readonly EgressProxyOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _stopping = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;

    public EgressProxyServer(EgressProxyOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _slots = new SemaphoreSlim(options.MaxConnections, options.MaxConnections);
    }

    /// <summary>Bound port (useful when started on port 0 in tests).</summary>
    public int Port => ((IPEndPoint)_listener!.LocalEndpoint).Port;

    public void Start(IPAddress? bind = null)
    {
        _listener = new TcpListener(bind ?? IPAddress.Any, _options.Port);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
        _logger.LogInformation("Egress proxy listening on {Port}; allowed ports {Ports}; private networks {Private}",
            Port, string.Join(",", _options.AllowedPorts), _options.AllowPrivateNetworks ? "ALLOWED" : "blocked");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(_stopping.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            if (!_slots.Wait(0))
            {
                await RejectAndCloseAsync(client, 503, "Proxy busy");
                continue;
            }
            _ = Task.Run(async () =>
            {
                try { await HandleAsync(client); }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                    _logger.LogDebug("Egress proxy connection ended: {Type}", ex.GetType().Name);
                }
                finally
                {
                    client.Dispose();
                    _slots.Release();
                }
            });
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        cts.CancelAfter(_options.IdleTimeout);
        var downstream = client.GetStream();

        var (head, extra) = await ReadHeadAsync(downstream, cts.Token);
        if (head is null) { await WriteStatusAsync(downstream, 400, "Bad request", cts.Token); return; }

        var firstLineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
        var requestLine = firstLineEnd < 0 ? head : head[..firstLineEnd];
        var parts = requestLine.Split(' ');
        if (parts.Length != 3) { await WriteStatusAsync(downstream, 400, "Bad request", cts.Token); return; }
        var (method, target) = (parts[0], parts[1]);

        if (method == "GET" && target == "/health")
        {
            await WriteStatusAsync(downstream, 200, "ok", cts.Token);
            return;
        }

        string host;
        int port;
        bool tunnel;
        string? rewrittenHead = null;
        if (method == "CONNECT")
        {
            var idx = target.LastIndexOf(':');
            if (idx <= 0 || !int.TryParse(target[(idx + 1)..], out port)) { await WriteStatusAsync(downstream, 400, "Bad CONNECT target", cts.Token); return; }
            host = target[..idx].Trim('[', ']');
            tunnel = true;
        }
        else if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp)
        {
            host = uri.IdnHost;
            port = uri.Port;
            tunnel = false;
            // Origin-form request line, without proxy headers.
            var headers = head[(firstLineEnd + 2)..].Split("\r\n")
                .Where(h => h.Length > 0 && !h.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase));
            rewrittenHead = $"{method} {uri.PathAndQuery} {parts[2]}\r\n{string.Join("\r\n", headers)}\r\n\r\n";
        }
        else
        {
            await WriteStatusAsync(downstream, 400, "Only CONNECT and absolute http:// URIs are supported", cts.Token);
            return;
        }

        var (upstream, reason) = await ConnectAsync(host, port, cts.Token);
        if (upstream is null)
        {
            _logger.LogWarning("Egress proxy BLOCKED {Host}:{Port} — {Reason}", host, port, reason);
            await WriteStatusAsync(downstream, 403, $"Blocked by egress policy: {reason}", cts.Token);
            return;
        }

        _logger.LogInformation("Egress proxy allowed {Host}:{Port} ({Mode})", host, port, tunnel ? "tunnel" : "http");
        using (upstream)
        {
            var upstreamStream = upstream.GetStream();
            if (tunnel)
            {
                await downstream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), cts.Token);
            }
            else
            {
                await upstreamStream.WriteAsync(Encoding.ASCII.GetBytes(rewrittenHead!), cts.Token);
            }
            if (extra.Length > 0) await upstreamStream.WriteAsync(extra, cts.Token);

            var a = PumpAsync(downstream, upstreamStream, cts);
            var b = PumpAsync(upstreamStream, downstream, cts);
            await Task.WhenAny(a, b);
            await cts.CancelAsync();
        }
    }

    /// <summary>Resolve, apply the policy to every candidate address, then connect to an allowed one.</summary>
    internal async Task<(TcpClient? Client, string Reason)> ConnectAsync(string host, int port, CancellationToken ct)
    {
        if (!_options.AllowedPorts.Contains(port)) return (null, $"port {port} is not allowed");

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException)
        {
            return (null, "host could not be resolved");
        }

        var allowed = _options.AllowPrivateNetworks ? addresses : addresses.Where(a => !NetworkPolicy.IsBlocked(a)).ToArray();
        if (allowed.Length == 0) return (null, "destination is on a private or internal network");

        foreach (var address in allowed)
        {
            var client = new TcpClient(address.AddressFamily);
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(_options.ConnectTimeout);
                await client.ConnectAsync(address, port, connectCts.Token);
                return (client, string.Empty);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                client.Dispose();
            }
        }
        return (null, "destination unreachable");
    }

    private static async Task<(string? Head, byte[] Extra)> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeaderBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) return (null, []);
            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                var headLength = end + 4;
                return (text[..headLength], buffer[headLength..total]);
            }
        }
        return (null, []);
    }

    private static async Task PumpAsync(Stream from, Stream to, CancellationTokenSource cts)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer, cts.Token)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                cts.CancelAfter(TimeSpan.FromMinutes(2)); // idle timer resets on traffic
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private static async Task WriteStatusAsync(Stream stream, int status, string message, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(message);
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 503 => "Service Unavailable", _ => "Error" };
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(head, ct);
            await stream.WriteAsync(body, ct);
        }
        catch (IOException) { }
    }

    private static async Task RejectAndCloseAsync(TcpClient client, int status, string message)
    {
        using (client)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await WriteStatusAsync(client.GetStream(), status, message, cts.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener?.Stop();
        if (_acceptLoop is not null) await _acceptLoop;
        _stopping.Dispose();
        _slots.Dispose();
    }
}
