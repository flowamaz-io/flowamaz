using System.Net;
using System.Net.Sockets;

namespace Flowamaz.Core.Security;

/// <summary>
/// Which destinations workflow-directed traffic may reach. Shared by the in-process egress guard,
/// the connector runner and the egress proxy so all three enforce the same rule.
/// Blocks loopback, RFC1918, link-local (incl. cloud metadata 169.254.169.254), CGNAT,
/// unique-local IPv6, unspecified, benchmarking, multicast and reserved ranges.
/// </summary>
public static class NetworkPolicy
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                   // 0.0.0.0/8
                || b[0] == 10                                  // 10/8
                || (b[0] == 172 && b[1] is >= 16 and <= 31)    // 172.16/12
                || (b[0] == 192 && b[1] == 168)                // 192.168/16
                || (b[0] == 169 && b[1] == 254)                // link-local + cloud metadata
                || (b[0] == 100 && b[1] is >= 64 and <= 127)   // CGNAT 100.64/10
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0/24 IETF
                || (b[0] == 198 && b[1] is 18 or 19)           // benchmarking 198.18/15
                || b[0] >= 224;                                // multicast + reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                       // fc00::/7 unique local
                || address.IsIPv6Teredo;
        }

        return true;
    }

    /// <summary>True when <paramref name="host"/> equals or is a subdomain of an allowed domain.</summary>
    public static bool HostAllowed(string host, IReadOnlyCollection<string>? allowedDomains)
    {
        if (allowedDomains is null || allowedDomains.Count == 0) return true;
        host = host.TrimEnd('.').ToLowerInvariant();
        foreach (var raw in allowedDomains)
        {
            var domain = raw.Trim().TrimStart('*', '.').TrimEnd('.').ToLowerInvariant();
            if (domain.Length == 0) continue;
            if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
