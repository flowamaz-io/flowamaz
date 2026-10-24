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

    public string? GetApiKeyAuditLabel() =>
        accessor.HttpContext?.Items[Core.Models.CurrentUserContext.HttpContextItemKey] is Core.Models.CurrentUserContext { IsApiKey: true } ctx
            ? ctx.ApiKeyAuditLabel ?? "API key"
            : null;

    public string? GetUserAuditLabel()
    {
        if (accessor.HttpContext?.Items[Core.Models.CurrentUserContext.HttpContextItemKey] is not Core.Models.CurrentUserContext { IsApiKey: false } ctx)
            return null;
        var name = string.IsNullOrWhiteSpace(ctx.Name) ? null : ctx.Name.Trim();
        var email = string.IsNullOrWhiteSpace(ctx.Email) ? null : ctx.Email.Trim();
        return name is not null && email is not null ? $"{name} ({email})" : name ?? email;
    }

    public Guid? GetUserId() =>
        accessor.HttpContext?.Items[Core.Models.CurrentUserContext.HttpContextItemKey] is Core.Models.CurrentUserContext { IsApiKey: false } ctx
            ? ctx.OrgUserId
            : null;

    public string? GetUserAgent()
    {
        var context = accessor.HttpContext;
        if (context is null) return null;
        var ua = context.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua) ? null : ua;
    }
}
