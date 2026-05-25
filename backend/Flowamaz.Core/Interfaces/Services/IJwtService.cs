using System.Security.Claims;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Issues and validates JWT access tokens and generates rotating refresh tokens.
/// Access tokens are HS256, 15-minute lifetime, carrying the org and active workspace memberships
/// (FUNCTIONAL.md §12.1). Refresh tokens are random 256-bit values; only their hash is stored.
/// </summary>
public interface IJwtService
{
    /// <summary>Builds a signed access token. <paramref name="orgSlug"/> is needed for the org_slug claim (not on OrgUser).</summary>
    string GenerateAccessToken(OrgUser user, string orgSlug, IReadOnlyList<WorkspaceMembership> memberships);

    /// <summary>Generates a new refresh token: the plain value (for the cookie) and its SHA256 hash (to store).</summary>
    (string PlainToken, string Hash) GenerateRefreshToken();

    /// <summary>SHA256 (hex, lower-case) of a plain refresh token — for hashing an incoming cookie before lookup.</summary>
    string HashRefreshToken(string plainToken);

    /// <summary>Validates signature, issuer, audience and lifetime. Returns null when invalid or expired.</summary>
    ClaimsPrincipal? ValidateAccessToken(string token);

    Guid ExtractOrgUserId(ClaimsPrincipal principal);
    Guid ExtractOrgId(ClaimsPrincipal principal);
    IReadOnlyList<WorkspaceMembership> ExtractWorkspaceMemberships(ClaimsPrincipal principal);
}
