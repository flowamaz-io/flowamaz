namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Abstracts the ambient HTTP request so non-web layers (Infrastructure) can read the caller's IP
/// and user-agent without depending on ASP.NET Core. Implemented in the Api layer over
/// IHttpContextAccessor. Returns null when there is no active request (background jobs, tests).
/// </summary>
public interface IRequestContextAccessor
{
    /// <summary>Caller IP behind the proxy: first hop of X-Forwarded-For, else the socket address.</summary>
    string? GetIpAddress();

    string? GetUserAgent();
}
