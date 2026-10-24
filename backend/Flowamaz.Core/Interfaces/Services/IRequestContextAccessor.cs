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

    /// <summary>
    /// When the current request authenticated with a workspace API key: its non-secret audit label
    /// (name + display prefix). Null for human (JWT) callers and outside a request.
    /// </summary>
    string? GetApiKeyAuditLabel() => null;

    /// <summary>"Name (email)" of the signed-in user for audit rows, or null outside a user request.</summary>
    string? GetUserAuditLabel() => null;

    /// <summary>Org user id of the signed-in human caller, or null (API key, background job, no request).</summary>
    Guid? GetUserId() => null;
}
