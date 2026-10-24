namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when login is attempted on a temporarily locked account (5 failed attempts → 15 min).
/// The message deliberately does NOT disclose the remaining lockout time (FUNCTIONAL.md §12.1).
/// </summary>
public sealed class AccountLockedException : AppException
{
    public AccountLockedException()
        : base("AUTH_ACCOUNT_LOCKED", "Sign-in is temporarily locked for this account after repeated failed attempts. " +
            "Wait a few minutes before signing in again, or use 'Forgot password' to reset your password now.", httpStatusCode: 401)
    {
    }
}
