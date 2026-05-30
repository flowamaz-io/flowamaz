using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>Identity claims extracted from an SSO assertion/token, normalised across SAML and OIDC.</summary>
public sealed record SsoClaims(string Email, string? Name);

/// <summary>SSO status for an org, returned to the login screen before a password is shown.</summary>
public sealed record SsoStatus(bool Enabled, string? Provider);

/// <summary>Result of a completed SSO login: the provisioned user and an issued Flowamaz JWT.</summary>
public sealed record SsoLoginResult(OrgUser User, string AccessToken, string OrgSlug);

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
    bool HasClientSecret);

/// <summary>Request to configure an org's SSO (provider + IdP details + secret material).</summary>
public sealed record ConfigureSsoRequest(
    string Provider,
    bool IsActive,
    string? IdpEntityId,
    string? IdpSsoUrl,
    string? IdpCertificate,
    string? IssuerUrl,
    string? ClientId,
    string? ClientSecret,
    IReadOnlyList<string>? Scopes);

/// <summary>
/// SSO for enterprise organisations (SAML 2.0 + OIDC). Org admins configure their IdP; users in
/// SSO-enabled orgs are redirected to the IdP on login and are JIT-provisioned on first sign-in
/// with the Viewer role. Password login still works for non-SSO orgs.
/// </summary>
public interface ISsoService
{
    Task<SsoStatus> GetStatusAsync(string orgSlug, CancellationToken ct = default);

    Task<SsoConfigDto?> GetConfigAsync(Guid orgId, CancellationToken ct = default);
    Task<SsoConfigDto> ConfigureAsync(Guid orgId, string orgSlug, ConfigureSsoRequest request, Guid configuredBy, CancellationToken ct = default);
    Task DisableAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>JIT provisioning: find the user by email in the org, or create one with the Viewer role.</summary>
    Task<OrgUser> ProvisionUserFromSsoAsync(string email, Guid orgId, SsoClaims claims, CancellationToken ct = default);

    // ── SAML ──────────────────────────────────────────────────────────────────
    Task<string> GetSpMetadataAsync(string orgSlug, CancellationToken ct = default);
    Task<string> InitiateSamlLoginAsync(string orgSlug, string? returnUrl, CancellationToken ct = default);
    Task<SsoLoginResult> HandleSamlCallbackAsync(string orgSlug, string samlResponse, CancellationToken ct = default);

    // ── OIDC ──────────────────────────────────────────────────────────────────
    Task<string> InitiateOidcLoginAsync(string orgSlug, string? returnUrl, CancellationToken ct = default);
    Task<SsoLoginResult> HandleOidcCallbackAsync(string orgSlug, string code, string state, CancellationToken ct = default);
}

/// <summary>Validates a SAML response (signature, audience, timestamp) and extracts claims, or throws.</summary>
public interface ISamlProcessor
{
    /// <summary>Builds the SP metadata XML for the given entity id and ACS URL.</summary>
    string BuildSpMetadata(string spEntityId, string acsUrl);

    /// <summary>Builds the redirect URL (with AuthnRequest) to the IdP SSO endpoint.</summary>
    string BuildAuthnRequestUrl(string idpSsoUrl, string spEntityId, string? relayState);

    /// <summary>Validates the signed SAML response against <paramref name="idpCertificatePem"/>; throws if invalid.</summary>
    SsoClaims ValidateResponse(string samlResponse, string idpCertificatePem, string expectedAudience);
}

/// <summary>Performs the OIDC authorization-code flow against an IdP discovered from its issuer.</summary>
public interface IOidcClient
{
    string BuildAuthorizeUrl(string issuerUrl, string clientId, string redirectUri, IReadOnlyList<string> scopes, string state);

    /// <summary>Exchanges the code for tokens and returns the validated identity claims.</summary>
    Task<SsoClaims> ExchangeCodeAsync(string issuerUrl, string clientId, string clientSecret, string redirectUri, string code, CancellationToken ct = default);
}

/// <summary>Short-lived store for the OIDC <c>state</c> CSRF token, mapping it to the org slug.</summary>
public interface IOidcStateStore
{
    Task<string> CreateAsync(string orgSlug, CancellationToken ct = default);

    /// <summary>Returns the org slug for a valid, unconsumed state, or null. Consumes the state.</summary>
    Task<string?> ConsumeAsync(string state, CancellationToken ct = default);
}
