namespace Flowamaz.Api.Validators;

/// <summary>
/// Shared allowlist policy for Stripe redirect URLs (Checkout success/cancel, Portal return).
/// A redirect target is accepted only when it is an absolute https URL whose host and port match the configured
/// <c>Platform:BaseUrl</c> (default ports normalised, so <c>:443</c> and no port are the same), or — in
/// Development — points at localhost. http is accepted only in Development, so a link can't downgrade the user to
/// plain http on the real host, and another port on the same host (another service) is refused. Everything else is
/// rejected so an attacker cannot craft a billing link that bounces a signed-in user to an
/// arbitrary site after Stripe.
/// </summary>
internal static class RedirectUrlPolicy
{
    public static bool IsAllowed(string? url, string? platformBaseUrl, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var secure = uri.Scheme == Uri.UriSchemeHttps;
        if (!secure && !(isDevelopment && uri.Scheme == Uri.UriSchemeHttp)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false; // https://user@host — never a legitimate return URL

        if (isDevelopment &&
            (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host == "127.0.0.1"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(platformBaseUrl)) return false;
        if (!Uri.TryCreate(platformBaseUrl, UriKind.Absolute, out var baseUri)) return false;

        return string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == baseUri.Port // Uri.Port fills in the scheme's default port
            && (secure || isDevelopment);
    }
}
