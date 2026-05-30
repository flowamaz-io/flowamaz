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

    [HttpGet("/api/v1/auth/sso/{orgSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<SsoStatus>> Status(string orgSlug, CancellationToken ct) =>
        Ok(await _sso.GetStatusAsync(orgSlug, ct));

    [HttpGet("/api/v1/saml/{orgSlug}/metadata")]
    [AllowAnonymous]
    public async Task<IActionResult> Metadata(string orgSlug, CancellationToken ct)
    {
        var xml = await _sso.GetSpMetadataAsync(orgSlug, ct);
        return Content(xml, "application/xml");
    }

    [HttpGet("/api/v1/saml/{orgSlug}/login")]
    [AllowAnonymous]
    public async Task<IActionResult> SamlLogin(string orgSlug, [FromQuery] string? returnUrl, CancellationToken ct) =>
        Redirect(await _sso.InitiateSamlLoginAsync(orgSlug, returnUrl, ct));

    [HttpPost("/api/v1/saml/{orgSlug}/acs")]
    [AllowAnonymous]
    public async Task<IActionResult> Acs(string orgSlug, [FromForm(Name = "SAMLResponse")] string samlResponse, CancellationToken ct)
    {
        var result = await _sso.HandleSamlCallbackAsync(orgSlug, samlResponse, ct);
        return Redirect($"{AppBaseUrl}/#sso_token={Uri.EscapeDataString(result.AccessToken)}");
    }

    // ── Org-owner configuration ──────────────────────────────────────────────

    [HttpGet("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<ActionResult<SsoConfigDto?>> GetConfig(Guid id, CancellationToken ct) =>
        Ok(await _sso.GetConfigAsync(id, ct));

    [HttpPut("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<ActionResult<SsoConfigDto>> Configure(Guid id, [FromBody] ConfigureSsoRequest request, CancellationToken ct)
    {
        var orgSlug = _currentUser.OrgSlug ?? string.Empty;
        var result = await _sso.ConfigureAsync(id, orgSlug, request, _currentUser.UserId ?? Guid.Empty, ct);
        return Ok(result);
    }

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

    [HttpDelete("/api/v1/organisations/{id:guid}/sso")]
    [RequireOrgOwner]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        await _sso.DisableAsync(id, ct);
        return NoContent();
    }
}
