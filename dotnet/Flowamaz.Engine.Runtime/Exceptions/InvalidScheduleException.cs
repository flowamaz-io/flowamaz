namespace Flowamaz.Core.Exceptions;

/// <summary>
/// A workflow declares a schedule trigger whose cron/time zone is missing or invalid, so it cannot be
/// published (it would never run). Maps to HTTP 422. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class InvalidScheduleException : AppException
{
    public InvalidScheduleException(string detail)
        : base(
            "INVALID_SCHEDULE",
            $"This workflow has a schedule trigger that cannot run: {detail} Fix the trigger's cron expression " +
            "(e.g. '0 9 * * 1-5' for 09:00 on weekdays) and publish again.",
            httpStatusCode: 422)
    {
    }
}
