using Flowamaz.Application.Auth.DTOs;
using Flowamaz.Application.Auth.Services;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Authentication endpoints. Access tokens are returned in the body; refresh tokens travel only in
/// the httpOnly <c>fmz_refresh</c> cookie (never the body) and rotate on every use (FUNCTIONAL.md §12.1).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookieName = "fmz_refresh";
    private const string CookiePath = "/api/v1/auth";
    private const int RegisterMaxPerHour = 5;
    private const int LoginMaxPerMinute = 10;

    private readonly AuthService _authService;
    private readonly IRateLimitService _rateLimit;
    private readonly IJwtService _jwtService;
    private readonly IHostEnvironment _environment;

    public AuthController(
        AuthService authService, IRateLimitService rateLimit, IJwtService jwtService, IHostEnvironment environment)
    {
        _authService = authService;
        _rateLimit = rateLimit;
        _jwtService = jwtService;
        _environment = environment;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        await EnforceRateLimitAsync($"register:{ClientIp}", RegisterMaxPerHour, windowSeconds: 3600, "registration", cancellationToken);

        var result = await _authService.RegisterAsync(request, ClientIp, cancellationToken);
        SetRefreshCookie(result.RefreshTokenPlain, result.RefreshTokenExpiresAt);
        return Ok(new RegisterResponse(result.AccessToken, result.ExpiresInSeconds, result.User));
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        await EnforceRateLimitAsync($"login:{ClientIp}", LoginMaxPerMinute, windowSeconds: 60, "login", cancellationToken);

        var result = await _authService.LoginAsync(request, ClientIp, cancellationToken);
        SetRefreshCookie(result.RefreshTokenPlain, result.RefreshTokenExpiresAt);
        return Ok(new LoginResponse(result.AccessToken, result.ExpiresInSeconds, result.User));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var cookie = Request.Cookies[RefreshCookieName];
        var result = await _authService.RefreshAsync(cookie, ClientIp, cancellationToken);
        if (result is null)
        {
            DeleteRefreshCookie();
            return StatusCode(401, new
            {
                success = false,
                statusCode = 401,
                code = "AUTH_INVALID_REFRESH",
                message = "Your session has expired. Please sign in again.",
                correlationId = CorrelationId,
            });
        }

        SetRefreshCookie(result.RefreshTokenPlain, result.RefreshTokenExpiresAt);
        return Ok(new RefreshResponse(result.AccessToken, result.ExpiresInSeconds));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(Request.Cookies[RefreshCookieName], cancellationToken);
        DeleteRefreshCookie();
        return Ok(new { loggedOut = true });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
    {
        var orgUserId = _jwtService.ExtractOrgUserId(User);
        if (orgUserId == Guid.Empty) return Unauthorized();

        var me = await _authService.GetMeAsync(orgUserId, cancellationToken);
        return Ok(me);
    }

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private string CorrelationId =>
        HttpContext.Items.TryGetValue("CorrelationId", out var v) && v is string s ? s : "";

    private async Task EnforceRateLimitAsync(string key, int max, int windowSeconds, string action, CancellationToken cancellationToken)
    {
        var allowed = await _rateLimit.CheckAndIncrementAsync(key, max, windowSeconds, cancellationToken);
        if (!allowed) throw new RateLimitExceededException(action);
    }

    private void SetRefreshCookie(string plainToken, DateTime expiresAt) =>
        Response.Cookies.Append(RefreshCookieName, plainToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = expiresAt,
        });

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
        });
}
