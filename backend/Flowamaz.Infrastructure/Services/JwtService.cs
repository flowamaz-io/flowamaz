using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// HS256 JWT issuing/validation and refresh-token generation (FUNCTIONAL.md §12.1). Uses the same
/// Jwt:Secret as the JwtBearer middleware so tokens this service mints validate against [Authorize].
/// Refresh tokens are 256-bit CSPRNG values; only their SHA256 hash leaves this class.
/// </summary>
public sealed class JwtService : IJwtService
{
    private const int MinSecretBytes = 32; // HS256 requires a >= 256-bit key
    private const string WorkspacesClaim = "workspaces";
    private const string OrgIdClaim = "org_id";
    private const string OrgSlugClaim = "org_slug";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly ILogger<JwtService> _logger;

    public JwtService(IOptions<JwtOptions> options, ILogger<JwtService> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (string.IsNullOrEmpty(_options.Secret) || Encoding.UTF8.GetByteCount(_options.Secret) < MinSecretBytes)
        {
            throw new InvalidOperationException(
                "Jwt:Secret (env JWT_SECRET) must be at least 32 bytes to sign access tokens. " +
                "Set a strong secret before issuing tokens.");
        }
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
    }

    public string GenerateAccessToken(OrgUser user, string orgSlug, IReadOnlyList<WorkspaceMembership> memberships)
    {
        _logger.LogDebug("JwtService.GenerateAccessToken enter orgUserId={UserId} workspaceCount={Count}", user.Id, memberships.Count);

        var workspacesJson = JsonSerializer.Serialize(
            memberships.Select(m => new
            {
                workspace_id = m.WorkspaceId,
                workspace_slug = m.WorkspaceSlug,
                workspace_name = m.WorkspaceName,
                role_name = m.RoleName,
                role_value = m.RoleValue,
            }),
            JsonOptions);

        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.Name),
            new("is_org_owner", user.IsOrgOwner.ToString().ToLowerInvariant()),
            new(OrgIdClaim, user.OrgId.ToString()),
            new(OrgSlugClaim, orgSlug),
            // Stored as a plain JSON string (not JsonArray) so it stays a single claim and round-trips
            // intact — a JsonArray value type would be split into one claim per element on read.
            new(WorkspacesClaim, workspacesJson),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.AccessTokenExpiryMinutes),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        _logger.LogDebug("JwtService.GenerateAccessToken exit orgUserId={UserId}", user.Id);
        return jwt;
    }

    public (string PlainToken, string Hash) GenerateRefreshToken()
    {
        var plain = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (plain, HashRefreshToken(plain));
    }

    public string HashRefreshToken(string plainToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken))).ToLowerInvariant();

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        _logger.LogDebug("JwtService.ValidateAccessToken enter");
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _options.Issuer,
                ValidAudience = _options.Audience,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
            var principal = handler.ValidateToken(token, parameters, out _);
            _logger.LogDebug("JwtService.ValidateAccessToken exit valid=true");
            return principal;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            _logger.LogDebug("JwtService.ValidateAccessToken exit valid=false reason={Reason}", ex.GetType().Name);
            return null;
        }
    }

    public Guid ExtractOrgUserId(ClaimsPrincipal principal) =>
        ParseGuidClaim(principal, JwtRegisteredClaimNames.Sub, ClaimTypes.NameIdentifier);

    public Guid ExtractOrgId(ClaimsPrincipal principal) =>
        ParseGuidClaim(principal, OrgIdClaim);

    public IReadOnlyList<WorkspaceMembership> ExtractWorkspaceMemberships(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(WorkspacesClaim)?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return [];

        try
        {
            var rows = JsonSerializer.Deserialize<List<WorkspaceClaimRow>>(raw, JsonOptions) ?? [];
            return rows
                .Select(r => new WorkspaceMembership(r.workspace_id, r.workspace_slug ?? "", r.workspace_name ?? "", r.role_name ?? "", r.role_value))
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JwtService.ExtractWorkspaceMemberships error — malformed workspaces claim; treating as none");
            return [];
        }
    }

    private static Guid ParseGuidClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var type in claimTypes)
        {
            var value = principal.FindFirst(type)?.Value;
            if (Guid.TryParse(value, out var id)) return id;
        }
        return Guid.Empty;
    }

    private sealed record WorkspaceClaimRow(Guid workspace_id, string? workspace_slug, string? workspace_name, string? role_name, int role_value);
}
