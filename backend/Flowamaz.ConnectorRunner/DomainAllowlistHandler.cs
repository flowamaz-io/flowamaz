using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Security;

namespace Flowamaz.ConnectorRunner;

/// <summary>
/// Refuses any outbound request whose host is not in the current operation's allowlist (e.g. the
/// Slack connector may only call slack.com). Private networks are additionally refused by the
/// egress proxy, so a connector can neither leave its vendor nor reach internal infrastructure.
/// </summary>
internal sealed class DomainAllowlistHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? string.Empty;
        if (!NetworkPolicy.HostAllowed(host, RunnerCallScope.AllowedDomains))
            throw new ConnectorOperationException(
                $"Blocked call to '{host}': this connector may only call {string.Join(", ", RunnerCallScope.AllowedDomains!)}.");
        return base.SendAsync(request, cancellationToken);
    }
}
