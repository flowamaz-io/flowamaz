using System.Text.Json;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services.Sso;

/// <summary>
/// OIDC authorization-code client. Discovers the IdP endpoints from its issuer's
/// <c>.well-known/openid-configuration</c>, exchanges the code at the token endpoint, and reads
/// email/name from the id_token payload. (Production hardening — full id_token signature
/// validation against the JWKS — is a documented follow-up.)
/// </summary>
public sealed class OidcClient : IOidcClient
{
    public const string HttpClientName = "oidc";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OidcClient> _logger;

    public OidcClient(IHttpClientFactory httpClientFactory, ILogger<OidcClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string BuildAuthorizeUrl(string issuerUrl, string clientId, string redirectUri, IReadOnlyList<string> scopes, string state)
    {
        var authorizeEndpoint = $"{issuerUrl.TrimEnd('/')}/authorize";
        var scope = Uri.EscapeDataString(string.Join(' ', scopes));
        return $"{authorizeEndpoint}?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={scope}&state={Uri.EscapeDataString(state)}";
    }

    public async Task<SsoClaims> ExchangeCodeAsync(
        string issuerUrl, string clientId, string clientSecret, string redirectUri, string code, CancellationToken ct = default)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        var tokenEndpoint = await DiscoverTokenEndpointAsync(http, issuerUrl, ct);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        using var response = await http.PostAsync(tokenEndpoint, form, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var tokens = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var idToken = tokens.RootElement.TryGetProperty("id_token", out var idt) ? idt.GetString() : null;
        if (string.IsNullOrWhiteSpace(idToken))
            throw new InvalidOperationException("OIDC token response did not contain an id_token.");

        var claims = DecodeIdTokenClaims(idToken);
        _logger.LogInformation("OidcClient.ExchangeCodeAsync ok email={Email}", claims.Email);
        return claims;
    }

    private static async Task<string> DiscoverTokenEndpointAsync(HttpClient http, string issuerUrl, CancellationToken ct)
    {
        var discoveryUrl = $"{issuerUrl.TrimEnd('/')}/.well-known/openid-configuration";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(discoveryUrl, ct));
        return doc.RootElement.GetProperty("token_endpoint").GetString()
            ?? throw new InvalidOperationException("OIDC discovery document has no token_endpoint.");
    }

    private static SsoClaims DecodeIdTokenClaims(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
            throw new InvalidOperationException("Malformed id_token.");

        var payload = Base64UrlDecode(parts[1]);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("id_token did not contain an email claim.");
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        return new SsoClaims(email, name);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
