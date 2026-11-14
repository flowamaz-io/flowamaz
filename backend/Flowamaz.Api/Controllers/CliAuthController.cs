using Flowamaz.Application.Auth.Services;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Sign-in for the `fmz` CLI (FUNCTIONAL.md §15, `fmz auth`). `fmz auth login` starts an OAuth-style device flow and
/// polls; the person approves in the browser where they are already signed in. The CLI then gets a session of its own:
/// a 15-minute access token plus a long-lived refresh token it trades for new access tokens (POST refresh). The person
/// sees their signed-in CLIs in Settings → CLI sessions and can end any of them; `fmz auth logout` ends its own.
/// </summary>
[ApiController]
[Route("api/v1/cli")]
public sealed class CliAuthController : ControllerBase
{
    private readonly ICliAuthService _cliAuth;
    private readonly CliSessionService _sessions;
    private readonly ICurrentUserService _currentUser;
    private readonly ITokenRevocationService? _revocation;

    public CliAuthController(
        ICliAuthService cliAuth,
        CliSessionService sessions,
        ICurrentUserService currentUser,
        ITokenRevocationService? revocation = null)
    {
        _cliAuth = cliAuth;
        _sessions = sessions;
        _currentUser = currentUser;
        _revocation = revocation;
    }

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>Starts a device flow. Anonymous — the CLI has no token yet. The body (the CLI's name) is optional.</summary>
    [HttpPost("auth/device")]
    public async Task<ActionResult<CliDeviceAuthorization>> Device(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CliDeviceRequest? request,
        CancellationToken cancellationToken)
    {
        var authorization = await _cliAuth.InitiateDeviceFlowAsync(request?.ClientName, cancellationToken);
        return Ok(authorization);
    }

    /// <summary>What the approval page shows about a pending login: the name the CLI gave itself.</summary>
    [HttpGet("auth/pending")]
    public async Task<IActionResult> Pending([FromQuery] string userCode, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.IsHumanUser)
            return Unauthorized(new { message = "Sign in to Flowamaz in this browser to approve a CLI login." });
        if (string.IsNullOrWhiteSpace(userCode))
            return BadRequest(new { message = "userCode query parameter is required. Use the code your terminal shows." });

        var name = await _cliAuth.GetPendingClientNameAsync(userCode.Trim(), cancellationToken);
        if (name is null)
            return NotFound(new { message = "That code is invalid, already used or has expired. Run `fmz auth login` again to get a fresh code." });
        return Ok(new { clientName = name });
    }

    /// <summary>
    /// Approves a pending flow for the signed-in person. Only a person signed in to the web app can approve — not an
    /// API key — and the CLI gets its own session for that person, not a copy of the browser's token.
    /// </summary>
    [HttpPost("auth/approve")]
    public async Task<IActionResult> Approve([FromBody] CliApproveRequest request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            return Unauthorized(new { message = "Sign in to approve a CLI login. Open the link that `fmz auth login` printed while signed in to Flowamaz." });
        if (!_currentUser.IsHumanUser)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "An API key can't approve a CLI login. Open the link `fmz auth login` printed in a browser where you're signed in to Flowamaz." });

        var approved = await _cliAuth.ApproveAsync((request.UserCode ?? string.Empty).Trim(), userId, cancellationToken);
        if (!approved)
            return BadRequest(new { message = "That code is invalid, already used or has expired. Run `fmz auth login` again to get a fresh code." });

        return Ok(new { status = "approved" });
    }

    /// <summary>
    /// Polls for the session. 202 while pending; 200 with the access token, the refresh token and the session once
    /// approved. The refresh token is in this response only — it is never shown again.
    /// </summary>
    [HttpGet("auth/token")]
    public async Task<IActionResult> Token([FromQuery] string deviceCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
            return BadRequest(new { message = "deviceCode query parameter is required." });

        var result = await _cliAuth.PollTokenAsync(deviceCode, cancellationToken);
        if (result.Pending || result.ApprovedByUserId is not { } userId)
            return StatusCode(StatusCodes.Status202Accepted, new { status = "pending" });

        var session = await _sessions.CreateAsync(userId, result.ClientName, ClientIp, cancellationToken);
        if (session is null)
            return Unauthorized(new
            {
                code = "CLI_SIGN_IN_REFUSED",
                message = "The account that approved this login can't sign in any more (it was deactivated or its organisation was removed). Ask an organisation owner, then run `fmz auth login` again.",
            });

        return Ok(new
        {
            accessToken = session.AccessToken,
            expiresAt = session.AccessTokenExpiresAt,
            refreshToken = session.RefreshToken,
            sessionId = session.SessionId,
            sessionExpiresAt = session.SessionExpiresAt,
        });
    }

    /// <summary>Trades the CLI's refresh token for a new 15-minute access token. Anonymous: the refresh token is the credential.</summary>
    [HttpPost("auth/refresh")]
    public async Task<IActionResult> Refresh([FromBody] CliRefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _sessions.RefreshAsync(request.RefreshToken, ClientIp, cancellationToken);
        if (result is null)
            return Unauthorized(new
            {
                code = "CLI_SESSION_ENDED",
                message = "This CLI session has ended: it was signed out, revoked in Settings → CLI sessions, or not used for a long time. Run `fmz auth login` to sign in again.",
            });

        return Ok(new
        {
            accessToken = result.AccessToken,
            expiresAt = result.AccessTokenExpiresAt,
            sessionId = result.SessionId,
            sessionExpiresAt = result.SessionExpiresAt,
        });
    }

    /// <summary>
    /// `fmz auth logout`: ends the session on the server so the token can't be used again. Also revokes the access token
    /// on the request, if any. Idempotent — an already-ended session still returns 200.
    /// </summary>
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CliRefreshRequest? request,
        CancellationToken cancellationToken)
    {
        var ended = await _sessions.LogoutAsync(request?.RefreshToken, cancellationToken);
        if (_revocation is not null && User.FindFirst("jti")?.Value is { } jti &&
            long.TryParse(User.FindFirst("exp")?.Value, out var exp))
            await _revocation.RevokeTokenAsync(jti, DateTimeOffset.FromUnixTimeSeconds(exp), cancellationToken);
        return Ok(new { loggedOut = true, sessionEnded = ended });
    }

    /// <summary>The caller's own signed-in CLIs (Settings → CLI sessions). People only — not API keys.</summary>
    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> ListSessions(CancellationToken cancellationToken)
    {
        if (!_currentUser.IsHumanUser || _currentUser.UserId is not { } userId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "CLI sessions belong to a person. Sign in to Flowamaz in the browser to see yours; API keys can't list them." });

        var sessions = await _sessions.ListAsync(userId, User.FindFirst("jti")?.Value, cancellationToken);
        return Ok(sessions);
    }

    /// <summary>Ends one of the caller's own CLI sessions. The CLI is signed out at once.</summary>
    [HttpDelete("sessions/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsHumanUser || _currentUser.UserId is not { } userId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "CLI sessions belong to a person. Sign in to Flowamaz in the browser to end one; API keys can't." });

        var revoked = await _sessions.RevokeAsync(userId, id, cancellationToken);
        if (!revoked)
            return NotFound(new { message = "That CLI session isn't active any more — it may already have been signed out or expired. Refresh the list to see your current sessions." });
        return Ok(new { revoked = true });
    }

    /// <summary>Informational browser landing page after approval.</summary>
    [HttpGet("auth/callback")]
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

public sealed record CliDeviceRequest(string? ClientName);
public sealed record CliApproveRequest(string UserCode);
public sealed record CliRefreshRequest(string? RefreshToken);
