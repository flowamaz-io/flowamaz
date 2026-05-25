namespace Flowamaz.Application.Auth.DTOs;

/// <summary>Register response body. The refresh token travels in an httpOnly cookie, never here.</summary>
public sealed record RegisterResponse(string AccessToken, int ExpiresIn, UserSummary User);

/// <summary>Refresh response body — a fresh access token; the rotated refresh token is in the cookie.</summary>
public sealed record RefreshResponse(string AccessToken, int ExpiresIn);

/// <summary>The authenticated user's profile and workspace memberships (GET /auth/me).</summary>
public sealed record MeResponse(
    Guid Id,
    string Email,
    string Name,
    Guid OrgId,
    string OrgSlug,
    IReadOnlyList<WorkspaceSummary> Workspaces);
