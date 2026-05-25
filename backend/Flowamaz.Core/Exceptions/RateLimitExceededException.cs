namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a client exceeds a fixed-window rate limit (e.g. login 10/min, register 5/hour).
/// Maps to HTTP 429. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class RateLimitExceededException : AppException
{
    public RateLimitExceededException(string action)
        : base(
            "RATE_LIMIT_EXCEEDED",
            $"Too many {action} attempts. Wait a little while before trying again.",
            httpStatusCode: 429)
    {
    }
}
