using Flowamaz.Api.Authorization;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// SSO: anonymous status check + SAML metadata/ACS, and org-owner-gated configuration. On a
/// successful SAML login the user is JIT-provisioned and redirected to the app with a short-lived
/// token fragment. Password login is unaffected for non-SSO orgs.
/// </summary>
[ApiController]
public sealed class SsoController : ControllerBase
{
    private readonly ISsoService _sso;
    private readonly ICurrentUserService _currentUser;
    private readonly IConfiguration _configuration;

    public SsoController(ISsoService sso, ICurrentUserService currentUser, IConfiguration configuration)
    {
        _sso = sso;
        _currentUser = currentUser;
        _configuration = configuration;
    }

    private string AppBaseUrl => _configuration["Platform:BaseUrl"]?.TrimEnd('/') ?? "https://app.flowamaz.io";

    /// <summary>Returns whether SSO is enabled for the given organisation slug so the login page can offer the SSO option. Anonymous.</summary>
    [HttpGet("/api/v1/auth/sso/{orgSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<SsoStatus>> Status(string orgSlug, CancellationToken ct) =>
        Ok(await _sso.GetStatusAsync(orgSlug, ct));

    /// <summary>Returns the SP (service provider) SAML metadata XML for the organisation, for configuring the IdP. Anonymous.</summary>
    [HttpGet("/api/v1/saml/{orgSlug}/metadata")]
    [AllowAnonymous]
    public async Task<IActionResult> Metadata(string orgSlug, CancellationToken ct)
    {
        var xml = await _sso.GetSpMetadataAsync(orgSlug, ct);
        return Content(xml, "application/xml");
    }

    /// <summary>Begins a SAML login by redirecting the browser to the organisation's identity provider. Anonymous.</summary>
    [HttpGet("/api/v1/saml/{orgSlug}/login")]
    [AllowAnonymous]
    public async Task<IActionResult> SamlLogin(string orgSlug, [FromQuery] string? returnUrl, CancellationToken ct) =>
        Redirect(await _sso.InitiateSamlLoginAsync(orgSlug, returnUrl, ct));

    /// <summary>SAML assertion consumer service: validates the IdP response, JIT-provisions the user, and redirects to the app with a short-lived token. Anonymous.</summary>
    [HttpPost("/api/v1/saml/{orgSlug}/acs")]
    [AllowAnonymous]
    public async Task<IActionResult> Acs(string orgSlug, [FromForm(Name = "SAMLResponse")] string samlResponse, CancellationToken ct)
    {
        var result = await _sso.HandleSamlCallbackAsync(orgSlug, samlResponse, ct);
        return Redirect($"{AppBaseUrl}/#sso_token={Uri.EscapeDataString(result.AccessToken)}");
    }

    // ── Org-owner configuration ──────────────────────────────────────────────

    /// <summary>Returns the organisation's SSO (SAML/OIDC) configuration, or null if not configured. Org-owner only; secrets are never returned.</summary>
    [HttpGet("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<ActionResult<SsoConfigDto?>> GetConfig(Guid id, CancellationToken ct) =>
        Ok(await _sso.GetConfigAsync(id, ct));

    /// <summary>Creates or updates the organisation's SSO (SAML/OIDC) configuration. Org-owner only.</summary>
    [HttpPut("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<ActionResult<SsoConfigDto>> Configure(Guid id, [FromBody] ConfigureSsoRequest request, CancellationToken ct)
    {
        var orgSlug = _currentUser.OrgSlug ?? string.Empty;
        var result = await _sso.ConfigureAsync(id, orgSlug, request, _currentUser.UserId ?? Guid.Empty, ct);
        return Ok(result);
    }

    /// <summary>Validates that the organisation's saved SSO configuration is complete and ready to activate. Org-owner only.</summary>
    [HttpPost("/api/v1/organisations/{id:guid}/sso/test")]
    [RequireOrgOwner]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        var config = await _sso.GetConfigAsync(id, ct);
        if (config is null)
            return Ok(new { ok = false, message = "SSO is not configured yet. Save a configuration first." });

        var ready = config.Provider == "saml"
            ? config is { IdpSsoUrl: not null, HasCertificate: true }
            : config is { IssuerUrl: not null, ClientId: not null, HasClientSecret: true };

        return Ok(new
        {
            ok = ready,
            message = ready
                ? $"{config.Provider.ToUpperInvariant()} configuration looks complete and ready to activate."
                : "Configuration is incomplete. Fill in all IdP fields before enabling SSO.",
        });
    }

    /// <summary>Disables SSO for the organisation, reverting members to password login. Org-owner only.</summary>
    [HttpDelete("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        await _sso.DisableAsync(id, ct);
        return NoContent();
    }
}
