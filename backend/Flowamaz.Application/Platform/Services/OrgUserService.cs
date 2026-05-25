using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Platform.Services;

/// <summary>
/// Org user credential validation and login-attempt bookkeeping. Passwords are BCrypt-verified;
/// neither the password nor the hash is ever written to a log.
/// </summary>
public sealed class OrgUserService : IOrgUserService
{
    private readonly IOrgUserRepository _orgUserRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OrgUserService> _logger;

    public OrgUserService(
        IOrgUserRepository orgUserRepository,
        IUnitOfWork unitOfWork,
        ILogger<OrgUserService> logger)
    {
        _orgUserRepository = orgUserRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<OrgUser?> GetByEmailAndOrgAsync(string email, Guid orgId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrgUserService.GetByEmailAndOrgAsync enter email={Email} orgId={OrgId}", email, orgId);
        try
        {
            var user = await _orgUserRepository.GetByEmailAndOrgAsync(email, orgId, cancellationToken);
            _logger.LogDebug("OrgUserService.GetByEmailAndOrgAsync exit orgId={OrgId} found={Found}", orgId, user is not null);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrgUserService.GetByEmailAndOrgAsync error email={Email} orgId={OrgId}", email, orgId);
            throw;
        }
    }

    public async Task<OrgUser?> ValidateCredentialsAsync(string email, Guid orgId, string password, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrgUserService.ValidateCredentialsAsync enter email={Email} orgId={OrgId}", email, orgId);
        try
        {
            var user = await _orgUserRepository.GetByEmailAndOrgAsync(email, orgId, cancellationToken);
            if (user is null || !user.IsActive || IsCurrentlyLockedOut(user) || !VerifyPassword(password, user))
            {
                _logger.LogDebug("OrgUserService.ValidateCredentialsAsync exit orgId={OrgId} authenticated=false", orgId);
                return null;
            }

            _logger.LogDebug("OrgUserService.ValidateCredentialsAsync exit orgId={OrgId} authenticated=true userId={UserId}", orgId, user.Id);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrgUserService.ValidateCredentialsAsync error email={Email} orgId={OrgId}", email, orgId);
            throw;
        }
    }

    public async Task UpdateLastLoginAsync(Guid orgUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrgUserService.UpdateLastLoginAsync enter userId={UserId}", orgUserId);
        try
        {
            var user = await _orgUserRepository.GetByIdAsync(orgUserId, cancellationToken);
            if (user is null) { LogMissing(nameof(UpdateLastLoginAsync), orgUserId); return; }

            user.LastLoginAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("OrgUserService.UpdateLastLoginAsync exit userId={UserId}", orgUserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrgUserService.UpdateLastLoginAsync error userId={UserId}", orgUserId);
            throw;
        }
    }

    public async Task RecordFailedLoginAsync(Guid orgUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrgUserService.RecordFailedLoginAsync enter userId={UserId}", orgUserId);
        try
        {
            var user = await _orgUserRepository.GetByIdAsync(orgUserId, cancellationToken);
            if (user is null) { LogMissing(nameof(RecordFailedLoginAsync), orgUserId); return; }

            user.FailedLoginCount += 1;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("OrgUserService.RecordFailedLoginAsync exit userId={UserId} failedCount={FailedCount}", orgUserId, user.FailedLoginCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrgUserService.RecordFailedLoginAsync error userId={UserId}", orgUserId);
            throw;
        }
    }

    public async Task<bool> IsLockedOutAsync(Guid orgUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrgUserService.IsLockedOutAsync enter userId={UserId}", orgUserId);
        try
        {
            var user = await _orgUserRepository.GetByIdAsync(orgUserId, cancellationToken);
            var lockedOut = user is not null && IsCurrentlyLockedOut(user);
            _logger.LogDebug("OrgUserService.IsLockedOutAsync exit userId={UserId} lockedOut={LockedOut}", orgUserId, lockedOut);
            return lockedOut;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrgUserService.IsLockedOutAsync error userId={UserId}", orgUserId);
            throw;
        }
    }

    public async Task LockAccountAsync(Guid orgUserId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrgUserService.LockAccountAsync enter userId={UserId} durationMinutes={Minutes}", orgUserId, duration.TotalMinutes);
        try
        {
            var user = await _orgUserRepository.GetByIdAsync(orgUserId, cancellationToken);
            if (user is null) { LogMissing(nameof(LockAccountAsync), orgUserId); return; }

            user.LockoutUntil = DateTime.UtcNow.Add(duration);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("OrgUserService.LockAccountAsync exit userId={UserId} lockoutUntil={LockoutUntil}", orgUserId, user.LockoutUntil);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrgUserService.LockAccountAsync error userId={UserId}", orgUserId);
            throw;
        }
    }

    public async Task ResetFailedLoginCountAsync(Guid orgUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("OrgUserService.ResetFailedLoginCountAsync enter userId={UserId}", orgUserId);
        try
        {
            var user = await _orgUserRepository.GetByIdAsync(orgUserId, cancellationToken);
            if (user is null) { LogMissing(nameof(ResetFailedLoginCountAsync), orgUserId); return; }

            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("OrgUserService.ResetFailedLoginCountAsync exit userId={UserId}", orgUserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrgUserService.ResetFailedLoginCountAsync error userId={UserId}", orgUserId);
            throw;
        }
    }

    private static bool IsCurrentlyLockedOut(OrgUser user) =>
        user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.UtcNow;

    private bool VerifyPassword(string password, OrgUser user)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        }
        catch (BCrypt.Net.SaltParseException ex)
        {
            // A malformed stored hash can never authenticate — treat as a failed match, not a crash.
            _logger.LogWarning(ex, "OrgUserService.VerifyPassword error userId={UserId} — stored hash is unparseable", user.Id);
            return false;
        }
    }

    private void LogMissing(string method, Guid orgUserId) =>
        _logger.LogWarning("OrgUserService.{Method} no-op userId={UserId} — user not found", method, orgUserId);
}
