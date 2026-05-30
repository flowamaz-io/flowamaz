namespace Flowamaz.Api.Validators;

/// <summary>
/// Shared allowlist policy for Stripe redirect URLs (Checkout success/cancel, Portal return).
/// A redirect target is accepted only when it is an absolute URL whose host matches the configured
/// <c>Platform:BaseUrl</c> host, or — in Development — points at localhost. Everything else is
/// rejected so an attacker cannot craft a billing link that bounces a signed-in user to an
/// arbitrary site after Stripe.
/// </summary>
internal static class RedirectUrlPolicy
{
    public static bool IsAllowed(string? url, string? platformBaseUrl, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        if (isDevelopment &&
            (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host == "127.0.0.1"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(platformBaseUrl)) return false;
        if (!Uri.TryCreate(platformBaseUrl, UriKind.Absolute, out var baseUri)) return false;

        return string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase);
    }
}
