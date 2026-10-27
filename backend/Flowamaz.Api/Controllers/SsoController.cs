using Flowamaz.Api.Authorization;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// SSO: anonymous status check + SAML metadata/login/ACS, and org-owner configuration of the caller's own
/// organisation. A successful sign-in starts a normal session (refresh cookie) — no token in the URL.
/// </summary>
[ApiController]
public sealed class SsoController : ControllerBase
{
    private readonly ISsoService _sso;
    private readonly ICurrentUserService _currentUser;
    private readonly IConfiguration _configuration;
    private readonly Application.Auth.Services.AuthService _auth;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SsoController> _logger;

    public SsoController(
        ISsoService sso, ICurrentUserService currentUser, IConfiguration configuration,
        Application.Auth.Services.AuthService auth, IHostEnvironment environment, ILogger<SsoController> logger)
    {
        _sso = sso;
        _currentUser = currentUser;
        _configuration = configuration;
        _auth = auth;
        _environment = environment;
        _logger = logger;
    }

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

    /// <summary>Begins a SAML login: redirects the browser to the organisation's IdP with an AuthnRequest. Anonymous.</summary>
    [HttpGet("/api/v1/saml/{orgSlug}/login")]
    [AllowAnonymous]
    public async Task<IActionResult> SamlLogin(string orgSlug, [FromQuery] string? returnUrl, CancellationToken ct)
    {
        try
        {
            return SsoSignInFlow.Start(this, _environment, await _sso.InitiateSamlLoginAsync(orgSlug, returnUrl, ct));
        }
        catch (Core.Exceptions.SsoLoginException ex)
        {
            return SsoSignInFlow.StartFailed(this, _configuration, orgSlug, ex);
        }
    }

    /// <summary>
    /// SAML assertion consumer service: validates the IdP's response, signs the user in (refresh cookie) and sends
    /// them into the app. Anonymous (the IdP posts here from the browser).
    /// </summary>
    [HttpPost("/api/v1/saml/{orgSlug}/acs")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public Task<IActionResult> Acs(
        string orgSlug, [FromForm(Name = "SAMLResponse")] string samlResponse, [FromForm(Name = "RelayState")] string? relayState,
        CancellationToken ct) =>
        SsoSignInFlow.CompleteAsync(this, orgSlug,
            binding => _sso.HandleSamlCallbackAsync(orgSlug, samlResponse, relayState, binding, ct),
            _auth, _environment, _configuration, _logger, ct);

    // ── Org-owner configuration ──────────────────────────────────────────────

    /// <summary>Returns the organisation's SSO (SAML/OIDC) configuration, or null if not configured. Org-owner only; secrets are never returned.</summary>
    [HttpGet("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<ActionResult<SsoConfigDto?>> GetConfig(Guid id, CancellationToken ct)
    {
        if (OtherOrg(id) is { } notFound) return notFound;
        return Ok(await _sso.GetConfigAsync(id, ct));
    }

    /// <summary>Creates or updates the organisation's SSO (SAML/OIDC) configuration. Org-owner only.</summary>
    [HttpPut("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<ActionResult<SsoConfigDto>> Configure(Guid id, [FromBody] ConfigureSsoRequest request, CancellationToken ct)
    {
        if (OtherOrg(id) is { } notFound) return notFound;
        var orgSlug = _currentUser.OrgSlug ?? string.Empty;
        var result = await _sso.ConfigureAsync(id, orgSlug, request, _currentUser.UserId ?? Guid.Empty, ct);
        return Ok(result);
    }

    /// <summary>
    /// "Check settings": validates the saved configuration and, for OIDC, contacts the IdP (discovery and signing keys).
    /// Org-owner only.
    /// </summary>
    [HttpPost("/api/v1/organisations/{id:guid}/sso/test")]
    [RequireOrgOwner]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        if (OtherOrg(id) is { } notFound) return notFound;
        var result = await _sso.TestAsync(id, ct);
        return Ok(new { ok = result.Ok, message = result.Message });
    }

    /// <summary>Disables SSO for the organisation, reverting members to password login. Org-owner only.</summary>
    [HttpDelete("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        if (OtherOrg(id) is { } notFound) return notFound;
        await _sso.DisableAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// The organisation in the route must be the caller's own. Without this, the owner of any organisation could read
    /// or replace another organisation's SSO configuration (pointing it at their own IdP) and sign in as its users.
    /// 404, not 403, so organisation ids can't be probed.
    /// </summary>
    private ObjectResult? OtherOrg(Guid id) =>
        _currentUser.OrgId == id
            ? null
            : NotFound(new
            {
                success = false,
                statusCode = 404,
                code = "ORG_NOT_FOUND",
                message = "No organisation with that id is available to you. Open SSO settings from your own organisation.",
            });
}
