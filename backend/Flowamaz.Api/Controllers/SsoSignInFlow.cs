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
/// </summary>
internal static class SsoSignInFlow
{
    public static string AppBaseUrl(IConfiguration configuration)
    {
        var configured = configuration["Platform:BaseUrl"];
        return string.IsNullOrWhiteSpace(configured) ? "https://app.flowamaz.io" : configured.TrimEnd('/');
    }

    public static async Task<IActionResult> CompleteAsync(
        ControllerBase controller, string orgSlug, Func<Task<SsoLoginResult>> signIn,
        AuthService auth, IHostEnvironment environment, IConfiguration configuration, ILogger logger, CancellationToken ct)
    {
        var app = AppBaseUrl(configuration);
        try
        {
            var result = await signIn();
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
