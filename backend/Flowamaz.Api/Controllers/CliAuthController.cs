using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// OAuth-style device flow for the `fmz` CLI (FUNCTIONAL.md §14). `fmz login` starts a flow and polls
/// for a token; the user approves in the browser where they are already signed in. The approve
/// endpoint binds the approving user's own access token to the device code.
/// </summary>
[ApiController]
[Route("api/v1/cli/auth")]
public sealed class CliAuthController : ControllerBase
{
    private readonly ICliAuthService _cliAuth;
    private readonly ICurrentUserService _currentUser;

    public CliAuthController(ICliAuthService cliAuth, ICurrentUserService currentUser)
    {
        _cliAuth = cliAuth;
        _currentUser = currentUser;
    }

    /// <summary>Starts a device flow. Anonymous — the CLI has no token yet.</summary>
    [HttpPost("device")]
    public async Task<ActionResult<CliDeviceAuthorization>> Device(CancellationToken cancellationToken)
    {
        var authorization = await _cliAuth.InitiateDeviceFlowAsync(cancellationToken);
        return Ok(authorization);
    }

    /// <summary>
    /// Approves a pending flow. Called by the signed-in SPA — the caller's bearer token is bound to
    /// the device code so the CLI collects the same identity.
    /// </summary>
    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] CliApproveRequest request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized(new { message = "Sign in to approve a CLI login. Open the link from `fmz login` while logged in to Flowamaz." });

        var header = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        var token = header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase) ? header[bearer.Length..].Trim() : string.Empty;
        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { message = "No bearer token on the request. The browser session must present its access token to approve a CLI login." });

        // The CLI token mirrors the approving user's access-token lifetime (15 minutes).
        var approved = await _cliAuth.ApproveAsync(request.UserCode, token, DateTime.UtcNow.AddMinutes(15), cancellationToken);
        if (!approved)
            return BadRequest(new { message = "That code is invalid or has expired. Run `fmz login` again to get a fresh code." });

        return Ok(new { status = "approved" });
    }

    /// <summary>Polls for the token. 202 while pending, 200 with the token once approved.</summary>
    [HttpGet("token")]
    public async Task<IActionResult> Token([FromQuery] string deviceCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
            return BadRequest(new { message = "deviceCode query parameter is required." });

        var result = await _cliAuth.PollTokenAsync(deviceCode, cancellationToken);
        if (result.Pending)
            return StatusCode(StatusCodes.Status202Accepted, new { status = "pending" });

        return Ok(new { accessToken = result.AccessToken, expiresAt = result.ExpiresAt });
    }

    /// <summary>Informational browser landing page after approval.</summary>
    [HttpGet("callback")]
    public ContentResult Callback() => new()
    {
        ContentType = "text/html; charset=utf-8",
        StatusCode = StatusCodes.Status200OK,
        Content = """
            <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Flowamaz CLI</title></head>
            <body style="font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;background:#0f172a;color:#e2e8f0">
            <div style="text-align:center"><div style="font-size:48px">✓</div>
            <h1 style="font-weight:600">CLI connected</h1>
            <p style="color:#94a3b8">You can close this tab and return to your terminal.</p></div>
            </body></html>
            """,
    };
}

public sealed record CliApproveRequest(string UserCode);
