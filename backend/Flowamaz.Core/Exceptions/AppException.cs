namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Base for all expected (business-rule) exceptions. Subclasses carry HTTP status hints
/// and an actionable error code that the GlobalExceptionMiddleware maps to a response.
/// Per CLAUDE.md rule 8: every error message is actionable — what happened + why + next step.
/// </summary>
public abstract class AppException : Exception
{
    /// <summary>Stable machine-readable code (e.g. "AI_CAPABILITY_MISMATCH"). Maps 1:1 to a help article.</summary>
    public string ErrorCode { get; }

    /// <summary>HTTP status code this exception should map to (default 400 Bad Request).</summary>
    public int HttpStatusCode { get; }

    protected AppException(string errorCode, string message, int httpStatusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }

    protected AppException(string errorCode, string message, Exception inner, int httpStatusCode = 400)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }
}
