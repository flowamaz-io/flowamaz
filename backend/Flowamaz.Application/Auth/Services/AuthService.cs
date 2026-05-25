using Flowamaz.Application.Auth.DTOs;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowamaz.Application.Auth.Services;

/// <summary>
/// Authentication orchestration: register, login (with lockout + no user enumeration), refresh
/// (with rotation), logout, and the /me view. Returns plain refresh tokens only to the controller
/// for the httpOnly cookie — never in a response body, never logged (FUNCTIONAL.md §12.1).
/// </summary>
public sealed class AuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Constant-time-ish guard against user enumeration: verify against a real hash even when the
    // user/org does not exist, so a missing account costs the same as a wrong password.
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("invalid-placeholder", 12);

    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IOrganisationService _organisationService;
    private readonly IOrgUserService _orgUserService;
    private readonly IOrgUserRepository _orgUserRepository;
    private readonly IWorkspaceMemberRepository _memberRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IJwtService _jwtService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IOrganisationService organisationService,
        IOrgUserService orgUserService,
        IOrgUserRepository orgUserRepository,
        IWorkspaceMemberRepository memberRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService,
        IUnitOfWork unitOfWork,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _organisationService = organisationService;
        _orgUserService = orgUserService;
        _orgUserRepository = orgUserRepository;
        _memberRepository = memberRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtService = jwtService;
        _unitOfWork = unitOfWork;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    private int AccessTokenExpirySeconds => _jwtOptions.AccessTokenExpiryMinutes * 60;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, string ip, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AuthService.RegisterAsync enter orgSlug={Slug} email={Email}", request.OrgSlug, request.Email);
        try
        {
            await _registerValidator.ValidateAndThrowAsync(request, cancellationToken);

            var result = await _organisationService.RegisterOrganisationAsync(
                request.OrgName, request.OrgSlug, request.BillingEmail, request.Email, request.Name,
                request.Password, request.PlanSlug, ParseRegion(request.DataRegion), cancellationToken);

            var owner = await _orgUserRepository.GetByIdAsync(result.OwnerUserId, cancellationToken)
                ?? throw new InvalidOperationException($"Owner user '{result.OwnerUserId}' missing immediately after registration.");

            var auth = await IssueTokensAsync(owner, result.OrgSlug, ip, cancellationToken);
            _logger.LogInformation("AuthService.RegisterAsync exit orgId={OrgId} ownerUserId={UserId}", result.OrgId, owner.Id);
            return auth;
        }
        catch (Exception ex) when (ex is not ValidationException and not SlugAlreadyExistsException)
        {
            _logger.LogError(ex, "AuthService.RegisterAsync error orgSlug={Slug}", request.OrgSlug);
            throw;
        }
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, string ip, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AuthService.LoginAsync enter orgSlug={Slug} email={Email}", request.OrgSlug, request.Email);
        try
        {
            await _loginValidator.ValidateAndThrowAsync(request, cancellationToken);

            var org = await _organisationService.GetBySlugAsync(request.OrgSlug, cancellationToken);
            if (org is null)
            {
                VerifyDummyPassword(request.Password); // equalise timing for unknown org
                throw new InvalidCredentialsException();
            }

            var user = await _orgUserService.GetByEmailAndOrgAsync(request.Email, org.Id, cancellationToken);
            if (user is null || !user.IsActive)
            {
                VerifyDummyPassword(request.Password); // equalise timing for unknown/disabled user
                throw new InvalidCredentialsException();
            }

            if (IsLockedOut(user))
            {
                throw new AccountLockedException();
            }

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                await _orgUserService.RecordFailedLoginAsync(user.Id, cancellationToken);
                if (user.FailedLoginCount >= MaxFailedAttempts)
                {
                    await _orgUserService.LockAccountAsync(user.Id, LockoutDuration, cancellationToken);
                    _logger.LogWarning("AuthService.LoginAsync account locked userId={UserId} after {Max} failed attempts", user.Id, MaxFailedAttempts);
                }
                throw new InvalidCredentialsException();
            }

            await _orgUserService.ResetFailedLoginCountAsync(user.Id, cancellationToken);
            await _orgUserService.UpdateLastLoginAsync(user.Id, cancellationToken);

            var auth = await IssueTokensAsync(user, org.Slug, ip, cancellationToken);
            _logger.LogInformation("AuthService.LoginAsync exit userId={UserId} orgId={OrgId} authenticated=true", user.Id, org.Id);
            return auth;
        }
        catch (Exception ex) when (ex is not ValidationException and not InvalidCredentialsException and not AccountLockedException)
        {
            _logger.LogError(ex, "AuthService.LoginAsync error orgSlug={Slug}", request.OrgSlug);
            throw;
        }
    }

    public async Task<AuthResult?> RefreshAsync(string? plainRefreshToken, string ip, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AuthService.RefreshAsync enter");
        try
        {
            if (string.IsNullOrWhiteSpace(plainRefreshToken)) return null;

            var hash = _jwtService.HashRefreshToken(plainRefreshToken);
            var token = await _refreshTokenRepository.GetByHashAsync(hash, cancellationToken);
            if (token is null || token.IsRevoked) return null;

            var user = await _orgUserRepository.GetByIdAsync(token.OrgUserId, cancellationToken);
            if (user is null || !user.IsActive) return null;

            var org = await _organisationService.GetByIdAsync(user.OrgId, cancellationToken);
            if (org is null) return null;

            // Rotation: revoke the presented token and issue a replacement atomically.
            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                token.RevokedAt = DateTime.UtcNow;
                var auth = await IssueTokensAsync(user, org.Slug, ip, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("AuthService.RefreshAsync exit userId={UserId} rotated=true", user.Id);
                return auth;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AuthService.RefreshAsync error");
            throw;
        }
    }

    public async Task LogoutAsync(string? plainRefreshToken, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AuthService.LogoutAsync enter");
        try
        {
            if (string.IsNullOrWhiteSpace(plainRefreshToken)) return;

            var token = await _refreshTokenRepository.GetByHashAsync(_jwtService.HashRefreshToken(plainRefreshToken), cancellationToken);
            if (token is null || token.RevokedAt.HasValue) return;

            token.RevokedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("AuthService.LogoutAsync exit userId={UserId} revoked=true", token.OrgUserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AuthService.LogoutAsync error");
            throw;
        }
    }

    public async Task<MeResponse> GetMeAsync(Guid orgUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AuthService.GetMeAsync enter userId={UserId}", orgUserId);
        try
        {
            var user = await _orgUserRepository.GetByIdAsync(orgUserId, cancellationToken)
                ?? throw new InvalidOperationException($"User '{orgUserId}' not found.");
            var org = await _organisationService.GetByIdAsync(user.OrgId, cancellationToken)
                ?? throw new InvalidOperationException($"Organisation '{user.OrgId}' not found.");
            var memberships = await _memberRepository.GetActiveMembershipsForUserAsync(orgUserId, cancellationToken);

            var workspaces = memberships
                .Select(m => new WorkspaceSummary(m.WorkspaceId, m.WorkspaceSlug, m.WorkspaceName, m.RoleName))
                .ToList();

            _logger.LogDebug("AuthService.GetMeAsync exit userId={UserId} workspaceCount={Count}", orgUserId, workspaces.Count);
            return new MeResponse(user.Id, user.Email, user.Name, user.OrgId, org.Slug, workspaces);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AuthService.GetMeAsync error userId={UserId}", orgUserId);
            throw;
        }
    }

    private async Task<AuthResult> IssueTokensAsync(OrgUser user, string orgSlug, string ip, CancellationToken cancellationToken)
    {
        var memberships = await _memberRepository.GetActiveMembershipsForUserAsync(user.Id, cancellationToken);
        var accessToken = _jwtService.GenerateAccessToken(user, orgSlug, memberships);
        var (plainRefresh, hash) = _jwtService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            OrgUserId = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpiryDays),
            CreatedByIp = ip,
            CreatedAt = DateTime.UtcNow,
        };
        await _refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            AccessTokenExpirySeconds,
            plainRefresh,
            refreshToken.ExpiresAt,
            new UserSummary(user.Id, user.Email, user.Name, user.OrgId, orgSlug));
    }

    private void VerifyDummyPassword(string password)
    {
        try { BCrypt.Net.BCrypt.Verify(password, DummyPasswordHash); }
        catch (BCrypt.Net.SaltParseException) { /* dummy hash is always valid; ignore defensively */ }
    }

    private static bool IsLockedOut(OrgUser user) =>
        user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.UtcNow;

    private static DataRegion ParseRegion(string? region) => (region ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "eu-west-1" => DataRegion.EuWest1,
        "us-east-1" => DataRegion.UsEast1,
        _ => DataRegion.ApSoutheast1,
    };
}
