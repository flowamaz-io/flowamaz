namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Audit-query validation failures (prompt 07-02). Carries an actionable message + HTTP status.
/// Used when a requested date range exceeds the allowed window.
/// </summary>
public sealed class AuditException : AppException
{
    public AuditException(string errorCode, string message, int httpStatusCode = 400)
        : base(errorCode, message, httpStatusCode)
    {
    }

    public static AuditException RangeTooWide(int maxDays) => new(
        "AUDIT_RANGE_TOO_WIDE",
        $"The requested date range exceeds the maximum of {maxDays} days. " +
        $"Narrow the from/to window to {maxDays} days or fewer and retry.",
        httpStatusCode: 400);

    public static AuditException InvalidRange() => new(
        "AUDIT_RANGE_INVALID",
        "The 'from' date must be on or before the 'to' date. Swap the values and retry.",
        httpStatusCode: 400);
}
