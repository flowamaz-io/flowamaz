using System.Net;
using System.Net.Sockets;

namespace Flowamaz.Application.Connectors.Security;

/// <summary>
/// SSRF protection for every outbound call a workflow can direct (HTTP action nodes and connector
/// handlers). The check runs on the resolved IP at connect time, so DNS rebinding and redirects to
/// internal hosts are blocked too — a hostname check alone would not be enough.
///
/// Blocked unless <c>Connectors:AllowPrivateNetworkEgress</c> is true (development and self-hosted
/// installs that intentionally call internal systems): loopback, RFC1918 private, link-local
/// (incl. cloud metadata 169.254.169.254), CGNAT, unique-local IPv6, unspecified and multicast.
/// FUNCTIONAL.md §9.5 also calls for an infrastructure egress proxy; this is the in-process floor.
/// </summary>
public static class EgressGuard
{
    public const string ConfigKey = "Connectors:AllowPrivateNetworkEgress";

    public static SocketsHttpHandler CreateHandler(bool allowPrivateNetworks) => new()
    {
        // Redirects are followed by the handler; every hop reconnects through ConnectCallback.
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);

            var allowed = allowPrivateNetworks ? addresses : addresses.Where(a => !IsBlocked(a)).ToArray();
            if (allowed.Length == 0)
                throw new HttpRequestException(
                    $"Blocked outbound request to '{context.DnsEndPoint.Host}': it resolves to a private or internal network address. " +
                    "Workflows can only call public endpoints. Self-hosted installs can allow internal targets with Connectors:AllowPrivateNetworkEgress.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsBlocked(IPAddress address) => Flowamaz.Core.Security.NetworkPolicy.IsBlocked(address);

    /// <summary>Rejects non-HTTP schemes before any connection is attempted.</summary>
    public static Uri RequireHttpUrl(string? url, string field = "url")
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new Flowamaz.Core.Exceptions.ConnectorOperationException(
                $"'{field}' must be an absolute http(s) URL, got '{url}'. Check the node configuration.");
        return uri;
    }
}
