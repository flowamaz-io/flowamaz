using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// OIDC authorization-code flow. <c>initiate</c> redirects to the IdP with a CSRF state; the IdP
/// returns to <c>callback</c>, where the state is validated, the code exchanged, the user
/// JIT-provisioned, and the browser redirected to the app with a short-lived token fragment.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class OidcController : ControllerBase
{
    private readonly ISsoService _sso;
    private readonly IConfiguration _configuration;

    public OidcController(ISsoService sso, IConfiguration configuration)
    {
        _sso = sso;
        _configuration = configuration;
    }

    private string AppBaseUrl => _configuration["Platform:BaseUrl"]?.TrimEnd('/') ?? "https://app.flowamaz.io";

    [HttpGet("/api/v1/oidc/{orgSlug}/initiate")]
    public async Task<IActionResult> Initiate(string orgSlug, [FromQuery] string? returnUrl, CancellationToken ct) =>
        Redirect(await _sso.InitiateOidcLoginAsync(orgSlug, returnUrl, ct));

    [HttpGet("/api/v1/oidc/{orgSlug}/callback")]
    public async Task<IActionResult> Callback(
        string orgSlug, [FromQuery] string code, [FromQuery] string state, CancellationToken ct)
    {
        var result = await _sso.HandleOidcCallbackAsync(orgSlug, code, state, ct);
        return Redirect($"{AppBaseUrl}/#sso_token={Uri.EscapeDataString(result.AccessToken)}");
    }
}
