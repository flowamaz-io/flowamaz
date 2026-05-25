namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown for any failed authentication — wrong org slug, wrong email, or wrong password.
/// All three produce this identical 401 so the API never reveals which field was wrong
/// (no user enumeration, FUNCTIONAL.md §12.1).
/// </summary>
public sealed class InvalidCredentialsException : AppException
{
    public InvalidCredentialsException()
        : base("AUTH_INVALID_CREDENTIALS", "Invalid credentials", httpStatusCode: 401)
    {
    }
}
