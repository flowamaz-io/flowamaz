namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown for any failed authentication — wrong org slug, wrong email, or wrong password.
/// All three produce this identical 401 so the API never reveals which field was wrong
/// (no user enumeration, FUNCTIONAL.md §12.1).
/// </summary>
public sealed class InvalidCredentialsException : AppException
{
    /// <summary>
    /// The single, enumeration-safe message returned for every failed sign-in. It says what happened,
    /// the possible causes (any of the three fields) and what to do next, without revealing which
    /// field was wrong or whether the email exists.
    /// </summary>
    public const string ActionableMessage =
        "Sign-in failed: the email, organisation URL and password don't match an account. " +
        "Check all three (the organisation URL is your organisation's short name, e.g. 'acme'), " +
        "or use 'Forgot password' to reset your password. " +
        "After 5 failed attempts the account is locked for 15 minutes.";

    public InvalidCredentialsException()
        : base("AUTH_INVALID_CREDENTIALS", ActionableMessage, httpStatusCode: 401)
    {
    }
}
