namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a password reset token is expired, already used, or not found.
/// Same message regardless of cause — prevents disclosure of token state.
/// </summary>
public sealed class InvalidResetTokenException : AppException
{
    public InvalidResetTokenException()
        : base("AUTH_INVALID_RESET_TOKEN",
               "This reset link has expired or has already been used. Request a new one from the forgot-password page.",
               httpStatusCode: 400)
    {
    }
}
