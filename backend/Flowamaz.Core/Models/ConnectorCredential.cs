namespace Flowamaz.Core.Models;

/// <summary>
/// A decrypted workspace credential handed to a connector operation handler for one call.
/// Resolved by <c>IConnectorCredentialResolver</c>; never persisted, logged or returned to a client.
/// <see cref="ToString"/> is redacted so an accidental log statement cannot leak the secret.
/// </summary>
/// <param name="Id">Vault credential id (safe to log).</param>
/// <param name="Name">Credential alias shown in the UI (safe to log).</param>
/// <param name="AuthType">e.g. "ApiKey", "BearerToken", "OAuth2AuthCode", "ConnectionString", "HmacSecret", "BasicAuth".</param>
/// <param name="Secret">Plain secret value. For OAuth2 this may be a JSON token bundle — use <see cref="AccessToken"/>.</param>
public sealed record ConnectorCredential(Guid Id, string Name, string AuthType, string Secret)
{
    /// <summary>
    /// The bearer token to send. OAuth credentials may be stored as a JSON bundle
    /// (<c>{"access_token":…,"refresh_token":…,"expires_at":…}</c>); plain tokens are returned as-is.
    /// </summary>
    public string AccessToken => OAuthTokenBundle.TryParse(Secret)?.AccessToken ?? Secret;

    public override string ToString() => $"ConnectorCredential {{ Id = {Id}, Name = {Name}, AuthType = {AuthType}, Secret = *** }}";
}

/// <summary>OAuth token bundle stored as a credential's plain value (JSON).</summary>
public sealed record OAuthTokenBundle(string AccessToken, string? RefreshToken, DateTime? ExpiresAtUtc)
{
    public bool IsExpired(DateTime utcNow, TimeSpan skew) => ExpiresAtUtc is { } exp && exp - skew <= utcNow;

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(new
    {
        access_token = AccessToken,
        refresh_token = RefreshToken,
        expires_at = ExpiresAtUtc?.ToString("O"),
    });

    public static OAuthTokenBundle? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.TrimStart()[0] != '{') return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(value);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var at) || at.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            var rt = root.TryGetProperty("refresh_token", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.String ? r.GetString() : null;
            DateTime? exp = root.TryGetProperty("expires_at", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String
                && DateTime.TryParse(e.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime() : null;
            return new OAuthTokenBundle(at.GetString()!, rt, exp);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
