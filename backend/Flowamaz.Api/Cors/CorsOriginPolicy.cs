namespace Flowamaz.Api.Cors;

/// <summary>
/// Resolves the CORS origin allowlist (prompt 08-05). In Production only the explicitly-configured
/// origins (plus the marketing site, which calls the API for the pricing page) are allowed — never
/// localhost. In Development a localhost fallback is used when nothing is configured.
/// </summary>
public static class CorsOriginPolicy
{
    // Dev-only fallback: Docker proxy ports + Vite dev servers (app 5173, marketing 5174).
    private static readonly string[] DevelopmentOrigins =
    [
        "http://localhost:8306",
        "http://localhost:8443",
        "https://localhost:8443",
        "http://localhost:3000",
        "http://localhost:5173",
        "http://localhost:5174",
    ];

    /// <summary>
    /// Builds the allowed-origins list. <paramref name="configuredOrigins"/> comes from
    /// CORS_ALLOWED_ORIGINS; <paramref name="marketingSiteUrl"/> from MARKETING_SITE_URL.
    /// </summary>
    public static string[] Resolve(bool isDevelopment, string[] configuredOrigins, string? marketingSiteUrl)
    {
        var origins = new List<string>();

        if (configuredOrigins.Length > 0)
        {
            origins.AddRange(configuredOrigins);
        }
        else if (isDevelopment)
        {
            origins.AddRange(DevelopmentOrigins);
        }
        // Production with no configured origins → empty list (all cross-origin blocked).

        // The marketing site (flowamaz.com) calls the API for the live pricing page in every
        // environment, so add it whenever it is configured.
        if (!string.IsNullOrWhiteSpace(marketingSiteUrl) && !origins.Contains(marketingSiteUrl))
        {
            origins.Add(marketingSiteUrl);
        }

        return origins.ToArray();
    }
}
