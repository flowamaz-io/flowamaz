namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when login is attempted on a temporarily locked account (5 failed attempts → 15 min).
/// The message deliberately does NOT disclose the remaining lockout time (FUNCTIONAL.md §12.1).
/// </summary>
public sealed class AccountLockedException : AppException
{
    public AccountLockedException()
        : base("AUTH_ACCOUNT_LOCKED", "Account temporarily locked. Try again later.", httpStatusCode: 401)
    {
    }
}
