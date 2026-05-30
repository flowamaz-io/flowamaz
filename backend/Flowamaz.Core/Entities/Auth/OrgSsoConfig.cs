using Flowamaz.Core.Entities;

namespace Flowamaz.Core.Entities.Auth;

/// <summary>
/// Single sign-on configuration for an organisation (one row per org). Supports SAML 2.0 and
/// OIDC. The IdP X.509 certificate (SAML) and client secret (OIDC) are encrypted at rest via
/// <c>ISecretProtector</c> using the org id as the key scope. SSO is only enforced when
/// <see cref="IsActive"/> is true.
/// </summary>
public class OrgSsoConfig : AuditableEntity
{
    public Guid OrgId { get; set; }

    /// <summary>"saml" or "oidc".</summary>
    public string Provider { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    // ── SAML ──────────────────────────────────────────────────────────────────
    public string? IdpEntityId { get; set; }
    public string? IdpSsoUrl { get; set; }

    /// <summary>X.509 signing certificate (PEM), encrypted at rest.</summary>
    public string? EncryptedIdpCertificate { get; set; }

    /// <summary>Generated: https://app.flowamaz.io/saml/{orgSlug}.</summary>
    public string? SpEntityId { get; set; }

    // ── OIDC ──────────────────────────────────────────────────────────────────
    public string? IssuerUrl { get; set; }
    public string? ClientId { get; set; }

    /// <summary>OIDC client secret, encrypted at rest.</summary>
    public string? EncryptedClientSecret { get; set; }

    public List<string> Scopes { get; set; } = ["openid", "email", "profile"];
}
