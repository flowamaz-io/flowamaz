using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowamaz.Application.Auth.Services;

/// <summary>What the CLI receives when a device-flow login completes. The refresh token is returned once, here only.</summary>
public sealed record CliSessionTokens(
    Guid SessionId,
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime SessionExpiresAt);

/// <summary>A fresh access token for an existing CLI session. The refresh token is not rotated.</summary>
public sealed record CliRefreshResult(
    Guid SessionId,
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    DateTime SessionExpiresAt);

/// <summary>A signed-in CLI as shown in Settings → CLI sessions. No token material.</summary>
public sealed record CliSessionDto(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    string? LastUsedIp,
    DateTime ExpiresAt,
    bool IsCurrent);

/// <summary>
/// Sessions for the `fmz` CLI (FUNCTIONAL.md §15, `fmz auth`). A device-flow approval creates one; the CLI then keeps
/// itself signed in by trading its refresh token for 15-minute access tokens, and ends it with `fmz auth logout`. The
/// person can see and end their CLI sessions in Settings. Ending a session also revokes the access token it last
/// handed out, so the terminal is cut off at once rather than up to 15 minutes later.
/// <para>
/// The plain refresh token is returned to the controller once at creation and never stored or logged; only its
/// SHA256 hash is persisted.
/// </para>
/// </summary>
public sealed class CliSessionService
{
    public const int MaxNameLength = 100;
    public const string DefaultName = "fmz CLI";

    private readonly ICliSessionRepository _sessions;
    private readonly IOrgUserRepository _users;
    private readonly IOrganisationService _organisations;
    private readonly IWorkspaceMemberRepository _members;
    private readonly IJwtService _jwt;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtOptions _options;
    private readonly ILogger<CliSessionService> _logger;
    private readonly ITokenRevocationService? _revocation;
    private readonly IAuditService? _audit;

    public CliSessionService(
        ICliSessionRepository sessions,
        IOrgUserRepository users,
        IOrganisationService organisations,
        IWorkspaceMemberRepository members,
        IJwtService jwt,
        IUnitOfWork unitOfWork,
        IOptions<JwtOptions> options,
        ILogger<CliSessionService> logger,
        ITokenRevocationService? revocation = null,
        IAuditService? audit = null)
    {
        _sessions = sessions;
        _users = users;
        _organisations = organisations;
        _members = members;
        _jwt = jwt;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
        _revocation = revocation;
        _audit = audit;
    }

    private TimeSpan IdleLifetime => TimeSpan.FromDays(Math.Max(1, _options.CliSessionIdleDays));

    /// <summary>
    /// Creates a session for the person who approved a device-flow login. Null when that person can no longer sign in
    /// (deactivated, or their organisation is gone) — the CLI is then told to sign in again.
    /// </summary>
    public async Task<CliSessionTokens?> CreateAsync(Guid orgUserId, string? name, string ip, CancellationToken ct = default)
    {
        _logger.LogDebug("CliSessionService.CreateAsync enter userId={UserId}", orgUserId);
        try
        {
            var user = await _users.GetByIdAsync(orgUserId, ct);
            if (user is null || !user.IsActive)
            {
                _logger.LogWarning("CliSessionService.CreateAsync exit user missing or inactive userId={UserId}", orgUserId);
                return null;
            }
            var org = await _organisations.GetByIdAsync(user.OrgId, ct);
            if (org is null)
            {
                _logger.LogWarning("CliSessionService.CreateAsync exit org missing userId={UserId}", orgUserId);
                return null;
            }

            var memberships = await _members.GetActiveMembershipsForUserAsync(user.Id, ct);
            var accessToken = _jwt.GenerateAccessToken(user, org.Slug, memberships);
            var (tokenId, accessExpires) = _jwt.ReadTokenIdentity(accessToken);
            var (plainRefresh, hash) = _jwt.GenerateRefreshToken();

            var now = DateTime.UtcNow;
            var session = new CliSession
            {
                OrgUserId = user.Id,
                OrgId = user.OrgId,
                TokenHash = hash,
                Name = CleanName(name),
                CreatedByIp = Truncate(ip, 64),
                LastUsedIp = Truncate(ip, 64),
                LastUsedAt = now,
                ExpiresAt = now.Add(IdleLifetime),
                CurrentAccessTokenId = tokenId,
                CurrentAccessTokenExpiresAt = accessExpires,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await _sessions.AddAsync(session, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            Audit(session, user.Id, "cli.session.created", "created", ct);
            _logger.LogInformation("CliSessionService.CreateAsync exit sessionId={SessionId} userId={UserId}", session.Id, user.Id);
            return new CliSessionTokens(session.Id, accessToken, accessExpires, plainRefresh, session.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliSessionService.CreateAsync error userId={UserId}", orgUserId);
            throw;
        }
    }

    /// <summary>
    /// Trades a CLI refresh token for a new access token and pushes the session's end date out again. Null when the
    /// token is unknown, the session was ended or expired, or the person can no longer sign in.
    /// </summary>
    public async Task<CliRefreshResult?> RefreshAsync(string? plainRefreshToken, string ip, CancellationToken ct = default)
    {
        _logger.LogDebug("CliSessionService.RefreshAsync enter");
        try
        {
            if (string.IsNullOrWhiteSpace(plainRefreshToken)) return null;

            var session = await _sessions.GetByHashAsync(_jwt.HashRefreshToken(plainRefreshToken.Trim()), ct);
            if (session is null || !session.IsActive)
            {
                _logger.LogInformation("CliSessionService.RefreshAsync exit refused reason={Reason}", session is null ? "unknown" : "ended");
                return null;
            }

            var user = await _users.GetByIdAsync(session.OrgUserId, ct);
            var org = user is null ? null : await _organisations.GetByIdAsync(user.OrgId, ct);
            if (user is null || !user.IsActive || org is null || user.OrgId != session.OrgId)
            {
                // The person can't sign in any more: end the session so it stops showing as live.
                session.RevokedAt = DateTime.UtcNow;
                session.UpdatedAt = session.RevokedAt.Value;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("CliSessionService.RefreshAsync exit refused reason=user-inactive sessionId={SessionId}", session.Id);
                return null;
            }

            var memberships = await _members.GetActiveMembershipsForUserAsync(user.Id, ct);
            var accessToken = _jwt.GenerateAccessToken(user, org.Slug, memberships);
            var (tokenId, accessExpires) = _jwt.ReadTokenIdentity(accessToken);

            var now = DateTime.UtcNow;
            session.CurrentAccessTokenId = tokenId;
            session.CurrentAccessTokenExpiresAt = accessExpires;
            session.LastUsedAt = now;
            session.LastUsedIp = Truncate(ip, 64);
            session.ExpiresAt = now.Add(IdleLifetime);
            session.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation("CliSessionService.RefreshAsync exit sessionId={SessionId}", session.Id);
            return new CliRefreshResult(session.Id, accessToken, accessExpires, session.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliSessionService.RefreshAsync error");
            throw;
        }
    }

    /// <summary>`fmz auth logout`: ends the session the presented refresh token belongs to. True if one was ended.</summary>
    public async Task<bool> LogoutAsync(string? plainRefreshToken, CancellationToken ct = default)
    {
        _logger.LogDebug("CliSessionService.LogoutAsync enter");
        try
        {
            if (string.IsNullOrWhiteSpace(plainRefreshToken)) return false;
            var session = await _sessions.GetByHashAsync(_jwt.HashRefreshToken(plainRefreshToken.Trim()), ct);
            if (session is null || session.RevokedAt.HasValue) return false;

            await EndAsync(session, ct);
            Audit(session, session.OrgUserId, "cli.session.revoked", "logout", ct);
            _logger.LogInformation("CliSessionService.LogoutAsync exit sessionId={SessionId}", session.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliSessionService.LogoutAsync error");
            throw;
        }
    }

    /// <summary>
    /// The caller's own live CLI sessions. <paramref name="currentAccessTokenId"/> (the jti of the request's token) marks
    /// the session making the call, when it is a CLI.
    /// </summary>
    public async Task<IReadOnlyList<CliSessionDto>> ListAsync(Guid orgUserId, string? currentAccessTokenId, CancellationToken ct = default)
    {
        _logger.LogDebug("CliSessionService.ListAsync enter userId={UserId}", orgUserId);
        try
        {
            var sessions = await _sessions.ListActiveForUserAsync(orgUserId, ct);
            var result = sessions.Select(s => new CliSessionDto(
                s.Id, s.Name, s.CreatedAt, s.LastUsedAt, s.LastUsedIp, s.ExpiresAt,
                currentAccessTokenId is not null && s.CurrentAccessTokenId == currentAccessTokenId)).ToList();
            _logger.LogDebug("CliSessionService.ListAsync exit userId={UserId} count={Count}", orgUserId, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliSessionService.ListAsync error userId={UserId}", orgUserId);
            throw;
        }
    }

    /// <summary>Ends one of the caller's own CLI sessions from Settings. False when there's no such live session.</summary>
    public async Task<bool> RevokeAsync(Guid orgUserId, Guid sessionId, CancellationToken ct = default)
    {
        _logger.LogDebug("CliSessionService.RevokeAsync enter userId={UserId} sessionId={SessionId}", orgUserId, sessionId);
        try
        {
            var session = await _sessions.GetForUserAsync(orgUserId, sessionId, ct);
            if (session is null || !session.IsActive)
            {
                _logger.LogInformation("CliSessionService.RevokeAsync exit not found sessionId={SessionId}", sessionId);
                return false;
            }

            await EndAsync(session, ct);
            Audit(session, orgUserId, "cli.session.revoked", "revoked", ct);
            _logger.LogInformation("CliSessionService.RevokeAsync exit sessionId={SessionId}", sessionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CliSessionService.RevokeAsync error sessionId={SessionId}", sessionId);
            throw;
        }
    }

    private async Task EndAsync(CliSession session, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        session.RevokedAt = now;
        session.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);

        // Cut off the access token this CLI holds now, not when it runs out.
        if (_revocation is not null && session.CurrentAccessTokenId is { Length: > 0 } jti &&
            session.CurrentAccessTokenExpiresAt is { } expires && expires > now)
        {
            await _revocation.RevokeTokenAsync(jti, new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)), ct);
        }
    }

    private void Audit(CliSession session, Guid actor, string eventType, string action, CancellationToken ct) =>
        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            OrgId = session.OrgId,
            ActorUserId = actor,
            ActorType = "user",
            EventType = eventType,
            ResourceType = "cli_session",
            ResourceId = session.Id,
            ResourceLabel = session.Name,
            Action = action,
        }, ct);

    /// <summary>Printable, single-line, at most <see cref="MaxNameLength"/> characters; a default when empty.</summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return DefaultName;
        var printable = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (printable.Length == 0) return DefaultName;
        return printable.Length > MaxNameLength ? printable[..MaxNameLength] : printable;
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length > max ? value[..max] : value;
}
