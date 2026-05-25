namespace Flowamaz.Application.Auth.DTOs;

/// <summary>Login response body. The refresh token travels in an httpOnly cookie, never here.</summary>
public sealed record LoginResponse(string AccessToken, int ExpiresIn, UserSummary User);
