using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Identity claims from a verified SSO assertion or id_token, normalised across SAML and OIDC.
/// <see cref="Subject"/> is the IdP's stable id for the person ("oidc|{iss}|{sub}" or "saml|{issuer}|{NameID}");
/// an account is linked to it on first SSO sign-in and must match it afterwards. <see cref="EmailVerified"/>: the IdP
/// vouches for the address (OIDC email_verified / Entra xms_edov; SAML from the org's own signed IdP).
/// </summary>
public sealed record SsoClaims(
    string Email, string? Name, IReadOnlyList<string>? Groups = null, string? Subject = null, bool EmailVerified = true)
{
    public IReadOnlyList<string> GroupList => Groups ?? [];
}

/// <summary>Where to send the browser to start an SSO sign-in, and the state key to bind to that browser.</summary>
public sealed record SsoRedirect(string Url, string StateKey);

/// <summary>
/// Ties an SSO sign-in to the browser that started it (login-CSRF protection): starting a sign-in sets a cookie
/// holding a hash of the state key, and the callback only proceeds when the cookie matches the returned state.
/// Without this, an attacker could hand a victim's browser the attacker's own IdP response and sign the victim in
/// as the attacker.
/// </summary>
public static class SsoBrowserBinding
{
    public const string CookieName = "fmz_sso";

    public static string For(string stateKey) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stateKey)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool Matches(string? cookieValue, string? stateKey)
    {
        if (string.IsNullOrEmpty(cookieValue) || string.IsNullOrEmpty(stateKey)) return false;
        var expected = System.Text.Encoding.ASCII.GetBytes(For(stateKey));
        var actual = System.Text.Encoding.ASCII.GetBytes(cookieValue);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

/// <summary>A SAML response that passed every check, with what replay protection needs.</summary>
public sealed record SamlAssertion(SsoClaims Claims, string AssertionId, DateTime NotOnOrAfterUtc, string? InResponseTo);

/// <summary>What the SP expects of a SAML response. Every field is checked.</summary>
public sealed record SamlExpectations(
    string IdpCertificatePem,
    string IdpEntityId,
    string SpEntityId,
    string AcsUrl,
    string GroupsAttribute);

/// <summary>A SAML AuthnRequest ready to send (HTTP-Redirect binding) and its id, for checking InResponseTo.</summary>
public sealed record SamlAuthnRequest(string RedirectUrl, string RequestId);

/// <summary>SSO status for an org, returned to the login screen before a password is shown.</summary>
public sealed record SsoStatus(bool Enabled, string? Provider, bool Required = false);

/// <summary>A completed SSO sign-in: the user, where to send the browser next, and the org.</summary>
public sealed record SsoLoginResult(OrgUser User, string OrgSlug, string ReturnPath);

/// <summary>Maps an IdP group to a role in one workspace, or in every workspace when <see cref="WorkspaceId"/> is null.</summary>
public sealed record SsoGroupRoleMapping(string Group, Guid? WorkspaceId, WorkspaceRole Role);

/// <summary>Admin-facing SSO configuration (never carries secrets/certificates).</summary>
public sealed record SsoConfigDto(
    Guid OrgId,
    string Provider,
    bool IsActive,
    string? IdpEntityId,
    string? IdpSsoUrl,
    string? SpEntityId,
    string? IssuerUrl,
    string? ClientId,
    IReadOnlyList<string> Scopes,
    bool HasCertificate,
    bool HasClientSecret,
    bool RequireSso = false,
    bool JitProvisioning = true,
    WorkspaceRole DefaultRole = WorkspaceRole.Viewer,
    string GroupsAttribute = "groups",
    IReadOnlyList<SsoGroupRoleMapping>? GroupRoleMappings = null,
    string? AcsUrl = null,
    string? MetadataUrl = null,
    string? OidcRedirectUri = null,
    bool PlanIncludesSso = true,
    bool AllowIdpInitiated = false,
    IReadOnlyList<string>? AllowedEmailDomains = null,
    IReadOnlyList<Guid>? DefaultWorkspaceIds = null);

/// <summary>Request to configure an org's SSO (provider, IdP details, secret material, access rules).</summary>
public sealed record ConfigureSsoRequest(
    string Provider,
    bool IsActive,
    string? IdpEntityId,
    string? IdpSsoUrl,
    string? IdpCertificate,
    string? IssuerUrl,
    string? ClientId,
    string? ClientSecret,
    IReadOnlyList<string>? Scopes,
    bool? RequireSso = null,
    bool? JitProvisioning = null,
    WorkspaceRole? DefaultRole = null,
    string? GroupsAttribute = null,
    IReadOnlyList<SsoGroupRoleMapping>? GroupRoleMappings = null,
    bool? AllowIdpInitiated = null,
    IReadOnlyList<string>? AllowedEmailDomains = null,
    IReadOnlyList<Guid>? DefaultWorkspaceIds = null);

/// <summary>
/// Single sign-on for Enterprise organisations (FUNCTIONAL.md §12.6): SAML 2.0 and OIDC, just-in-time
/// provisioning (optional), IdP group → workspace role mapping, and "require SSO", which blocks password sign-in
/// for everyone except organisation owners (so a broken IdP can't lock the organisation out).
/// </summary>
public interface ISsoService
{
    Task<SsoStatus> GetStatusAsync(string orgSlug, CancellationToken ct = default);

    Task<SsoConfigDto?> GetConfigAsync(Guid orgId, CancellationToken ct = default);
    Task<SsoConfigDto> ConfigureAsync(Guid orgId, string orgSlug, ConfigureSsoRequest request, Guid configuredBy, CancellationToken ct = default);
    Task DisableAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>
    /// Finds the user by email in the org (or creates one when JIT provisioning is on), applies group → role
    /// mappings, and returns the user. Throws <see cref="Exceptions.SsoLoginException"/> when access is refused.
    /// </summary>
    Task<OrgUser> ProvisionUserFromSsoAsync(string email, Guid orgId, SsoClaims claims, CancellationToken ct = default);

    /// <summary>True when the org requires SSO and this user may not use a password (everyone but org owners).</summary>
    Task<bool> IsPasswordLoginBlockedAsync(Guid orgId, OrgUser user, CancellationToken ct = default);

    // ── SAML ──────────────────────────────────────────────────────────────────
    Task<string> GetSpMetadataAsync(string orgSlug, CancellationToken ct = default);
    Task<SsoRedirect> InitiateSamlLoginAsync(string orgSlug, string? returnUrl, CancellationToken ct = default);
    Task<SsoLoginResult> HandleSamlCallbackAsync(string orgSlug, string samlResponse, string? relayState, string? browserBinding, CancellationToken ct = default);

    // ── OIDC ──────────────────────────────────────────────────────────────────
    Task<SsoRedirect> InitiateOidcLoginAsync(string orgSlug, string? returnUrl, CancellationToken ct = default);
    Task<SsoLoginResult> HandleOidcCallbackAsync(string orgSlug, string code, string state, string? browserBinding, CancellationToken ct = default);
}

/// <summary>SAML 2.0 service-provider protocol work. Throws <see cref="Exceptions.SsoLoginException"/> on any failed check.</summary>
public interface ISamlProcessor
{
    /// <summary>Builds the SP metadata XML for the given entity id and ACS URL.</summary>
    string BuildSpMetadata(string spEntityId, string acsUrl);

    /// <summary>
    /// Builds a SAML AuthnRequest with <paramref name="requestId"/> for the HTTP-Redirect binding (deflated, base64,
    /// URL-encoded). The response's InResponseTo must later equal the id.
    /// </summary>
    SamlAuthnRequest BuildAuthnRequest(string idpSsoUrl, string spEntityId, string acsUrl, string requestId, string relayState);

    /// <summary>
    /// Validates a SAML response: status, exactly one assertion, an XML signature (RSA-SHA256 or stronger) by the
    /// IdP certificate that covers the assertion actually read, issuer, audience, recipient and validity window.
    /// </summary>
    SamlAssertion ValidateResponse(string samlResponse, SamlExpectations expected);
}

/// <summary>An OIDC provider's discovery document (the parts the client uses).</summary>
public sealed record OidcDiscovery(string Issuer, string AuthorizationEndpoint, string TokenEndpoint, string JwksUri);

/// <summary>OIDC authorization-code flow with PKCE and a nonce, against an IdP discovered from its issuer.</summary>
public interface IOidcClient
{
    Task<OidcDiscovery> DiscoverAsync(string issuerUrl, CancellationToken ct = default);

    string BuildAuthorizeUrl(OidcDiscovery discovery, string clientId, string redirectUri, IReadOnlyList<string> scopes,
        string state, string nonce, string codeChallenge);

    /// <summary>
    /// Exchanges the code (with the PKCE verifier) and validates the id_token: signature against the IdP's JWKS,
    /// issuer, audience, expiry and nonce. Returns the identity claims or throws.
    /// </summary>
    Task<SsoClaims> ExchangeCodeAsync(OidcDiscovery discovery, string clientId, string clientSecret, string redirectUri,
        string code, string codeVerifier, string nonce, string groupsClaim, CancellationToken ct = default);
}

/// <summary>What an SSO sign-in carries from the start (redirect to the IdP) to the callback.</summary>
public sealed record SsoLoginState(
    string OrgSlug,
    string Protocol,
    string ReturnPath,
    string? SamlRequestId = null,
    string? OidcNonce = null,
    string? OidcCodeVerifier = null);

/// <summary>Short-lived, single-use store for sign-ins in progress (OIDC state / SAML RelayState).</summary>
public interface ISsoStateStore
{
    Task<string> CreateAsync(SsoLoginState state, CancellationToken ct = default);

    /// <summary>Returns and deletes the state for <paramref name="key"/>, or null if unknown, expired or used.</summary>
    Task<SsoLoginState?> ConsumeAsync(string key, CancellationToken ct = default);
}

/// <summary>Remembers SAML assertion ids until they expire, so the same assertion can't be used twice.</summary>
public interface ISsoReplayCache
{
    /// <summary>True the first time an id is seen; false if it was already used.</summary>
    Task<bool> TryRecordAsync(string orgSlug, string assertionId, DateTime expiresAtUtc, CancellationToken ct = default);
}
