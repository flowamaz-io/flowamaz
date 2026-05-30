using Flowamaz.Core.Interfaces.Services;

namespace Flowamaz.Api.Identity;

/// <summary>
/// Reads the caller's IP / user-agent from the ambient HttpContext for audit capture (prompt 07-02).
/// IP is resolved behind the nginx proxy via the first hop of X-Forwarded-For, falling back to the
/// socket address. Returns null outside a request (background jobs).
/// </summary>
public sealed class HttpRequestContextAccessor(IHttpContextAccessor accessor) : IRequestContextAccessor
{
    public string? GetIpAddress()
    {
        var context = accessor.HttpContext;
        if (context is null) return null;

        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var first = forwarded.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first)) return first;
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    public string? GetUserAgent()
    {
        var context = accessor.HttpContext;
        if (context is null) return null;
        var ua = context.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua) ? null : ua;
    }
}
