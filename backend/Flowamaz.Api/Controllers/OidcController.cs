using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// OIDC authorization-code flow with PKCE. <c>initiate</c> redirects to the IdP; the IdP returns to <c>callback</c>,
/// where the sign-in is matched to its state, the code exchanged and the id_token validated, the user signed in
/// (refresh cookie) and sent into the app.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class OidcController : ControllerBase
{
    private readonly ISsoService _sso;
    private readonly IConfiguration _configuration;
    private readonly Application.Auth.Services.AuthService _auth;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OidcController> _logger;

    public OidcController(
        ISsoService sso, IConfiguration configuration, Application.Auth.Services.AuthService auth,
        IHostEnvironment environment, ILogger<OidcController> logger)
    {
        _sso = sso;
        _configuration = configuration;
        _auth = auth;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet("/api/v1/oidc/{orgSlug}/initiate")]
    public async Task<IActionResult> Initiate(string orgSlug, [FromQuery] string? returnUrl, CancellationToken ct)
    {
        try
        {
            return Redirect(await _sso.InitiateOidcLoginAsync(orgSlug, returnUrl, ct));
        }
        catch (SsoLoginException ex)
        {
            return SsoSignInFlow.StartFailed(this, _configuration, orgSlug, ex);
        }
    }

    [HttpGet("/api/v1/oidc/{orgSlug}/callback")]
    public Task<IActionResult> Callback(
        string orgSlug, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            // The IdP refused or the user cancelled; the IdP's own error text isn't shown (it isn't ours to vouch for).
            _logger.LogInformation("OIDC callback without a code org={OrgSlug} error={Error}", orgSlug, error);
            return Task.FromResult(SsoSignInFlow.StartFailed(this, _configuration, orgSlug,
                SsoLoginException.Rejected("the identity provider didn't complete the sign-in.")));
        }
        return SsoSignInFlow.CompleteAsync(this, orgSlug, () => _sso.HandleOidcCallbackAsync(orgSlug, code, state, ct),
            _auth, _environment, _configuration, _logger, ct);
    }
}
