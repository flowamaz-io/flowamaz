using Flowamaz.Core.Entities;

using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;

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

    // ── Access rules (FUNCTIONAL.md §12.6) ───────────────────────────────────────

    /// <summary>
    /// "Require SSO": password sign-in is refused for everyone in the org except org owners, who keep it so a
    /// misconfigured IdP can't lock the organisation out.
    /// </summary>
    public bool RequireSso { get; set; }

    /// <summary>Create an account on first SSO sign-in. Off: only people who already have an account can sign in.</summary>
    public bool JitProvisioning { get; set; } = true;

    /// <summary>Role a just-in-time account gets in <see cref="DefaultWorkspaceIds"/> when no group mapping applies.</summary>
    public WorkspaceRole DefaultRole { get; set; } = WorkspaceRole.Viewer;

    /// <summary>Workspaces a just-in-time account joins (with <see cref="DefaultRole"/>). Empty: none until granted.</summary>
    public List<Guid> DefaultWorkspaceIds { get; set; } = [];

    /// <summary>
    /// Accept SAML responses the IdP sends unprompted (an app tile in Okta/Entra). Off by default: such responses
    /// can't be tied to the browser, which allows login CSRF.
    /// </summary>
    public bool AllowIdpInitiated { get; set; }

    /// <summary>
    /// Take workspace roles from the person's SCIM groups instead of the groups in the sign-in. Turn on once the IdP
    /// pushes groups over SCIM; until then the sign-in's groups decide (creating a SCIM token doesn't change this).
    /// </summary>
    public bool RolesFromScim { get; set; }

    /// <summary>Email domains an SSO sign-in may use (e.g. "acme.com"). Empty: any domain the IdP vouches for.</summary>
    public List<string> AllowedEmailDomains { get; set; } = [];

    /// <summary>SAML attribute / OIDC claim carrying the user's IdP groups.</summary>
    public string GroupsAttribute { get; set; } = "groups";

    /// <summary>IdP group → workspace role. Applied on every SSO sign-in; the highest matching role wins.</summary>
    public List<SsoGroupRoleMapping> GroupRoleMappings { get; set; } = [];
}
