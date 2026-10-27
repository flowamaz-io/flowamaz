using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flowamaz.Infrastructure.Services.Sso;

/// <summary>
/// OIDC authorization-code client with PKCE (S256) and a nonce. Endpoints come from the issuer's discovery
/// document, whose <c>issuer</c> must equal the configured one. The id_token is validated in full: signature
/// against the IdP's JWKS (asymmetric algorithms only), issuer, audience (the client id), expiry (2 minutes' skew)
/// and nonce. An email the IdP marks unverified is refused. Discovery documents and key sets are cached for an hour
/// and refreshed once when a token is signed with an unknown key (key rotation).
/// <para>
/// The issuer URL is typed by an org owner, so every call is server-side request forgery surface: the named HTTP
/// client (see <see cref="CreateHandler"/>) refuses private and internal addresses at connect time, follows no
/// redirects, times out quickly and caps response size; every endpoint must be https. <c>Sso:AllowInsecureIdp</c>
/// lifts both rules for local development against an IdP on localhost.
/// </para>
/// </summary>
public sealed class OidcClient : IOidcClient
{
    public const string HttpClientName = "oidc";
    public const string AllowInsecureConfigKey = "Sso:AllowInsecureIdp";
    public const int MaxResponseBytes = 512 * 1024;
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, (OidcDiscovery Doc, DateTime Until)> Discoveries = new();
    private static readonly ConcurrentDictionary<string, (JsonWebKeySet Keys, DateTime Until)> KeySets = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OidcClient> _logger;
    private readonly TimeProvider _clock;
    private readonly bool _allowInsecure;

    public OidcClient(IHttpClientFactory httpClientFactory, ILogger<OidcClient> logger, IConfiguration? configuration = null, TimeProvider? clock = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _allowInsecure = configuration?.GetValue<bool>(AllowInsecureConfigKey) ?? false;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Handler for the OIDC HTTP client: blocks private/internal addresses on the resolved IP (so DNS rebinding is
    /// covered), never follows redirects.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(bool allowPrivateNetworks) => new()
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var allowed = allowPrivateNetworks ? addresses : addresses.Where(a => !NetworkPolicy.IsBlocked(a)).ToArray();
            if (allowed.Length == 0)
                throw new HttpRequestException($"Blocked request to '{context.DnsEndPoint.Host}': it resolves to a private or internal address.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>The configured issuer must be an absolute https URL (http only when insecure IdPs are allowed).</summary>
    public static bool IsAcceptableIssuerUrl(string? url, bool allowInsecure) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (allowInsecure && uri.Scheme == Uri.UriSchemeHttp))
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);

    private void RequireSecure(string url, string what)
    {
        if (!IsAcceptableIssuerUrl(url, _allowInsecure))
            throw new SsoLoginException("SSO_IDP_MISCONFIGURED",
                $"Your identity provider's {what} must be an https URL (got '{url}'). Check the issuer URL in SSO settings.", 400);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<OidcDiscovery> DiscoverAsync(string issuerUrl, CancellationToken ct = default)
    {
        var issuer = issuerUrl.Trim();
        if (Discoveries.TryGetValue(issuer, out var cached) && cached.Until > Now) return cached.Doc;

        _logger.LogDebug("OidcClient.DiscoverAsync enter issuer={Issuer}", issuer);
        RequireSecure(issuer, "issuer URL");
        var http = _httpClientFactory.CreateClient(HttpClientName);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(await http.GetStringAsync($"{issuer.TrimEnd('/')}/.well-known/openid-configuration", ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("OidcClient.DiscoverAsync failed issuer={Issuer}: {Error}", issuer, ex.Message);
            throw new SsoLoginException("SSO_IDP_UNREACHABLE",
                "Flowamaz couldn't read your identity provider's OpenID configuration. Check the issuer URL in SSO settings and that the IdP is reachable.", 502);
        }

        using (doc)
        {
            string Required(string name) =>
                doc.RootElement.TryGetProperty(name, out var v) && v.GetString() is { Length: > 0 } s ? s
                    : throw new SsoLoginException("SSO_IDP_MISCONFIGURED", $"Your identity provider's OpenID configuration has no '{name}'.", 502);

            var discovery = new OidcDiscovery(
                Required("issuer"), Required("authorization_endpoint"), Required("token_endpoint"), Required("jwks_uri"));
            if (!string.Equals(discovery.Issuer.TrimEnd('/'), issuer.TrimEnd('/'), StringComparison.Ordinal))
                throw new SsoLoginException("SSO_IDP_MISCONFIGURED",
                    $"The issuer URL in SSO settings ('{issuer}') doesn't match what the identity provider reports ('{discovery.Issuer}'). Copy the issuer exactly.", 400);

            RequireSecure(discovery.AuthorizationEndpoint, "authorization endpoint");
            RequireSecure(discovery.TokenEndpoint, "token endpoint");
            RequireSecure(discovery.JwksUri, "key set (jwks_uri)");

            Discoveries[issuer] = (discovery, Now.Add(CacheFor));
            return discovery;
        }
    }

    public string BuildAuthorizeUrl(OidcDiscovery discovery, string clientId, string redirectUri, IReadOnlyList<string> scopes,
        string state, string nonce, string codeChallenge)
    {
        var scope = scopes.Contains("openid") ? scopes : ["openid", .. scopes];
        var separator = discovery.AuthorizationEndpoint.Contains('?') ? '&' : '?';
        return $"{discovery.AuthorizationEndpoint}{separator}response_type=code" +
               $"&client_id={Uri.EscapeDataString(clientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&scope={Uri.EscapeDataString(string.Join(' ', scope))}" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&nonce={Uri.EscapeDataString(nonce)}" +
               $"&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256";
    }

    public async Task<SsoClaims> ExchangeCodeAsync(OidcDiscovery discovery, string clientId, string clientSecret, string redirectUri,
        string code, string codeVerifier, string nonce, string groupsClaim, CancellationToken ct = default)
    {
        _logger.LogDebug("OidcClient.ExchangeCodeAsync enter issuer={Issuer}", discovery.Issuer);
        var http = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code_verifier"] = codeVerifier,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        string? idToken;
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                // Log the IdP's error code only; the body may echo secrets.
                var error = TryReadString(body, "error") ?? ((int)response.StatusCode).ToString();
                _logger.LogWarning("OidcClient.ExchangeCodeAsync token endpoint refused: {Error}", error);
                throw SsoLoginException.Rejected($"the identity provider refused the sign-in code ({error}). Check the client id and secret in SSO settings.");
            }
            idToken = TryReadString(body, "id_token");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SsoLoginException("SSO_IDP_UNREACHABLE", "Flowamaz couldn't reach your identity provider to finish signing in. Try again.", 502);
        }
        if (string.IsNullOrWhiteSpace(idToken))
            throw SsoLoginException.Rejected("the identity provider returned no id_token. Make sure the 'openid' scope is allowed.");

        var token = await ValidateIdTokenAsync(idToken, discovery, clientId, nonce, ct);

        // Only the 'email' claim, and only when the IdP vouches for it: 'upn' and unverified addresses can be set by
        // users or tenant admins elsewhere and would let them sign in as someone else.
        var email = Claim(token, "email");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw SsoLoginException.Rejected("the id_token has no email. Add the 'email' scope/claim for the Flowamaz app in your IdP.");
        if (!IsTrue(token, "email_verified") && !IsTrue(token, "xms_edov"))
            throw SsoLoginException.EmailUnverified(); // Entra: add the optional 'xms_edov' claim (see SSO help)
        var sub = Claim(token, "sub")
            ?? throw SsoLoginException.Rejected("the id_token has no subject ('sub').");

        var name = Claim(token, "name")
            ?? string.Join(' ', new[] { Claim(token, "given_name"), Claim(token, "family_name") }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var groups = Groups(token, groupsClaim);

        _logger.LogInformation("OidcClient.ExchangeCodeAsync ok groups={GroupCount}", groups.Count);
        return new SsoClaims(email.Trim().ToLowerInvariant(), string.IsNullOrWhiteSpace(name) ? null : name, groups,
            Subject: $"oidc|{discovery.Issuer}|{sub}", EmailVerified: true);
    }

    private async Task<JsonWebToken> ValidateIdTokenAsync(string idToken, OidcDiscovery discovery, string clientId, string nonce, CancellationToken ct)
    {
        var handler = new JsonWebTokenHandler();
        TokenValidationResult result = await ValidateWithKeysAsync(await KeysAsync(discovery.JwksUri, refresh: false, ct));
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
            result = await ValidateWithKeysAsync(await KeysAsync(discovery.JwksUri, refresh: true, ct)); // key rotation

        if (!result.IsValid)
        {
            _logger.LogWarning("OidcClient id_token invalid: {Error}", result.Exception?.GetType().Name);
            throw SsoLoginException.Rejected(result.Exception switch
            {
                SecurityTokenExpiredException => "the identity token has expired.",
                SecurityTokenInvalidAudienceException => "the identity token was issued for a different app (client id mismatch).",
                SecurityTokenInvalidIssuerException => "the identity token comes from a different issuer than the one configured.",
                _ => "the identity token's signature or contents are invalid.",
            });
        }

        var token = (JsonWebToken)result.SecurityToken;
        if (!token.TryGetPayloadValue<string>("nonce", out var tokenNonce) || !string.Equals(tokenNonce, nonce, StringComparison.Ordinal))
            throw SsoLoginException.Rejected("the identity token doesn't belong to this sign-in (nonce mismatch).");
        return token;

        Task<TokenValidationResult> ValidateWithKeysAsync(JsonWebKeySet keys) => handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = discovery.Issuer,
            ValidAudience = clientId,
            IssuerSigningKeys = keys.GetSigningKeys(),
            ValidAlgorithms =
            [
                SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
                SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
                SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
            ],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            LifetimeValidator = (notBefore, expires, _, p) =>
                expires is { } e && Now - p.ClockSkew < e && (notBefore is not { } nb || Now + p.ClockSkew >= nb),
        });
    }

    public async Task<int> CountSigningKeysAsync(OidcDiscovery discovery, CancellationToken ct = default) =>
        (await KeysAsync(discovery.JwksUri, refresh: true, ct)).GetSigningKeys().Count;

    private async Task<JsonWebKeySet> KeysAsync(string jwksUri, bool refresh, CancellationToken ct)
    {
        if (!refresh && KeySets.TryGetValue(jwksUri, out var cached) && cached.Until > Now) return cached.Keys;
        try
        {
            var json = await _httpClientFactory.CreateClient(HttpClientName).GetStringAsync(jwksUri, ct);
            var keys = new JsonWebKeySet(json);
            KeySets[jwksUri] = (keys, Now.Add(CacheFor));
            return keys;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            _logger.LogWarning("OidcClient.KeysAsync failed: {Error}", ex.GetType().Name);
            throw new SsoLoginException("SSO_IDP_UNREACHABLE", "Flowamaz couldn't read your identity provider's signing keys. Try again shortly.", 502);
        }
    }

    private static bool IsTrue(JsonWebToken token, string name)
    {
        if (token.TryGetPayloadValue<bool>(name, out var flag)) return flag;
        return token.TryGetPayloadValue<string>(name, out var text) && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Claim(JsonWebToken token, string name) =>
        token.TryGetPayloadValue<string>(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static List<string> Groups(JsonWebToken token, string groupsClaim)
    {
        foreach (var name in new[] { groupsClaim, "groups", "roles" }.Distinct(StringComparer.Ordinal))
        {
            if (token.TryGetPayloadValue<string[]>(name, out var many) && many is { Length: > 0 })
                return many.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.Ordinal).ToList();
            if (token.TryGetPayloadValue<string>(name, out var one) && !string.IsNullOrWhiteSpace(one))
                return [one];
        }
        return [];
    }

    private static string? TryReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Test hook: forget cached discovery documents and key sets.</summary>
    internal static void ClearCaches()
    {
        Discoveries.Clear();
        KeySets.Clear();
    }
}
