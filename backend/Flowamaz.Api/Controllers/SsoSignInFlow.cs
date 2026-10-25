using Flowamaz.Api.Identity;
using Flowamaz.Application.Auth.Services;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// The end of every SSO sign-in (SAML ACS, OIDC callback): start the same session as password sign-in (refresh
/// cookie; the web app picks it up with its usual silent refresh) and send the browser back into the app. A refused
/// sign-in goes back to the login page with an error code the page explains — never the raw reason, and never a token
/// in the URL.
/// <para>
/// Starting a sign-in sets the short-lived <see cref="SsoBrowserBinding.CookieName"/> cookie; the callback passes it to
/// the service, which refuses a response that this browser didn't start (login CSRF). Outside development it is
/// SameSite=None + Secure, because the SAML response arrives as a cross-site POST from the IdP.
/// </para>
/// </summary>
internal static class SsoSignInFlow
{
    public static string AppBaseUrl(IConfiguration configuration)
    {
        var configured = configuration["Platform:BaseUrl"];
        return string.IsNullOrWhiteSpace(configured) ? "https://app.flowamaz.io" : configured.TrimEnd('/');
    }

    private const string BindingPath = "/api/v1";

    public static IActionResult Start(ControllerBase controller, IHostEnvironment environment, SsoRedirect redirect)
    {
        controller.Response.Cookies.Append(SsoBrowserBinding.CookieName, SsoBrowserBinding.For(redirect.StateKey),
            BindingCookieOptions(environment, DateTimeOffset.UtcNow.AddMinutes(10)));
        return controller.Redirect(redirect.Url);
    }

    private static CookieOptions BindingCookieOptions(IHostEnvironment environment, DateTimeOffset? expires)
    {
        var secure = !environment.IsDevelopment();
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            // Local development runs on http://localhost, where the IdP is same-site and Lax is enough.
            SameSite = secure ? SameSiteMode.None : SameSiteMode.Lax,
            Path = BindingPath,
            Expires = expires,
            IsEssential = true,
        };
    }

    public static async Task<IActionResult> CompleteAsync(
        ControllerBase controller, string orgSlug, Func<string?, Task<SsoLoginResult>> signIn,
        AuthService auth, IHostEnvironment environment, IConfiguration configuration, ILogger logger, CancellationToken ct)
    {
        var app = AppBaseUrl(configuration);
        var binding = controller.Request.Cookies[SsoBrowserBinding.CookieName];
        controller.Response.Cookies.Delete(SsoBrowserBinding.CookieName, BindingCookieOptions(environment, null));
        try
        {
            var result = await signIn(binding);
            var ip = controller.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var session = await auth.IssueSsoSessionAsync(result.User, result.OrgSlug, ip, ct);
            RefreshCookie.Append(controller.Response, environment, session.RefreshTokenPlain, session.RefreshTokenExpiresAt);
            return controller.Redirect(app + result.ReturnPath);
        }
        catch (SsoLoginException ex)
        {
            logger.LogWarning("SSO sign-in refused org={OrgSlug} code={Code}", orgSlug, ex.ErrorCode);
            return controller.Redirect($"{app}/login?org={Uri.EscapeDataString(orgSlug)}&sso_error={Uri.EscapeDataString(ex.ErrorCode)}");
        }
    }

    public static IActionResult StartFailed(ControllerBase controller, IConfiguration configuration, string orgSlug, SsoLoginException ex) =>
        controller.Redirect($"{AppBaseUrl(configuration)}/login?org={Uri.EscapeDataString(orgSlug)}&sso_error={Uri.EscapeDataString(ex.ErrorCode)}");
}
