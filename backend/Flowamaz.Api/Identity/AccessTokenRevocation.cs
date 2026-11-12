using System.Security.Claims;
using Flowamaz.Core.Interfaces.Services;

namespace Flowamaz.Api.Identity;

/// <summary>
/// The revocation check shared by the two places a JWT is accepted: ASP.NET's JwtBearer handler (what [Authorize]
/// trusts) and <see cref="Middleware.JwtAuthMiddleware"/> (what the current-user service reads). Both must refuse a
/// revoked token, or a request could pass [Authorize] without a current user. The answer is cached on the request,
/// so Redis is asked once.
/// </summary>
public static class AccessTokenRevocation
{
    private const string ItemKey = "fmz:access_token_revoked";
    private const string IssuedAtMsClaim = "iat_ms"; // JwtService.IssuedAtMsClaim

    /// <summary>True when the token these claims came from was issued before a revocation marker for its user or org.</summary>
    public static async Task<bool> IsRevokedAsync(
        HttpContext context, IEnumerable<Claim> claims, ITokenRevocationService revocation, bool useCache = true)
    {
        var list = claims as IReadOnlyCollection<Claim> ?? claims.ToList();
        string? Value(params string[] types) => list.FirstOrDefault(c => types.Contains(c.Type))?.Value;

        // Cached per token (subject + issue time), so a second token in the same request is checked on its own.
        var cacheKey = $"{ItemKey}:{Value("sub", ClaimTypes.NameIdentifier)}:{Value(IssuedAtMsClaim)}";
        if (useCache && context.Items.TryGetValue(cacheKey, out var cached) && cached is bool known) return known;

        var revoked = false;
        if (Guid.TryParse(Value("sub", ClaimTypes.NameIdentifier), out var userId))
        {
            _ = Guid.TryParse(Value("org_id"), out var orgId);
            var isOwner = Value("is_org_owner") == "true";
            // Tokens from before this claim existed count as issued at 0, so any marker refuses them.
            _ = long.TryParse(Value(IssuedAtMsClaim), out var issuedAtMs);
            revoked = await revocation.IsRevokedAsync(userId, orgId, isOwner, issuedAtMs, context.RequestAborted);
        }
        context.Items[cacheKey] = revoked;
        return revoked;
    }
}
