namespace Flowamaz.Api.Identity;

/// <summary>The httpOnly refresh-token cookie shared by password sign-in, refresh and SSO sign-in.</summary>
public static class RefreshCookie
{
    public const string Name = "fmz_refresh";
    public const string Path = "/api/v1/auth";

    public static void Append(HttpResponse response, IHostEnvironment environment, string plainToken, DateTime expiresAt) =>
        response.Cookies.Append(Name, plainToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            // Lax (not Strict): the SAML ACS is a cross-site POST from the IdP, and OIDC returns by a top-level GET.
            SameSite = SameSiteMode.Lax,
            Path = Path,
            Expires = expiresAt,
        });

    public static void Delete(HttpResponse response, IHostEnvironment environment) =>
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = Path,
        });
}
