namespace Flowamaz.Application.Auth.DTOs;

/// <summary>
/// Internal result of register/login/refresh passed from <c>AuthService</c> to the controller.
/// <see cref="RefreshTokenPlain"/> is set into the httpOnly cookie by the controller and is NEVER
/// written to the HTTP response body (FUNCTIONAL.md §12.1).
/// </summary>
public sealed record AuthResult(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshTokenPlain,
    DateTime RefreshTokenExpiresAt,
    UserSummary User);
