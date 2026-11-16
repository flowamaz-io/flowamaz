using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Gates;

/// <summary>
/// The public base URL used in links the runtime sends out (gate approve/reject links, approval cards).
/// Reads the same setting as the rest of the app first (<c>Platform:BaseUrl</c>, env <c>Platform__BaseUrl</c>),
/// then the older <c>PLATFORM_URL</c> / <c>PlatformUrl</c>. Without any of them it falls back to the
/// production site and logs a warning, so a dev or staging server that forgot the setting is visible in
/// its logs instead of quietly sending people to production.
/// </summary>
public static class PlatformUrl
{
    public const string ProductionDefault = "https://app.flowamaz.com";

    public static string Resolve(IConfiguration? config, ILogger? logger = null)
    {
        var configured = config?["Platform:BaseUrl"] ?? config?["PLATFORM_URL"] ?? config?["PlatformUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().TrimEnd('/');

        logger?.LogWarning(
            "No public base URL is configured (Platform:BaseUrl / PLATFORM_URL), so links in gate emails and cards point to {Default}. " +
            "Set Platform__BaseUrl to this server's address (for local development: http://localhost:5173).",
            ProductionDefault);
        return ProductionDefault;
    }
}
