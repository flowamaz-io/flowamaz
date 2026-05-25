using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Org user lookups, credential validation, and login-attempt bookkeeping (lockout support
/// per FUNCTIONAL.md §12.1). Passwords are BCrypt-verified; hashes are never logged.
/// </summary>
public interface IOrgUserService
{
    Task<OrgUser?> GetByEmailAndOrgAsync(string email, Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>Returns the user when active, not locked out, and the password matches; otherwise null.</summary>
    Task<OrgUser?> ValidateCredentialsAsync(string email, Guid orgId, string password, CancellationToken cancellationToken = default);

    Task UpdateLastLoginAsync(Guid orgUserId, CancellationToken cancellationToken = default);
    Task RecordFailedLoginAsync(Guid orgUserId, CancellationToken cancellationToken = default);
    Task<bool> IsLockedOutAsync(Guid orgUserId, CancellationToken cancellationToken = default);
    Task LockAccountAsync(Guid orgUserId, TimeSpan duration, CancellationToken cancellationToken = default);
    Task ResetFailedLoginCountAsync(Guid orgUserId, CancellationToken cancellationToken = default);
}
