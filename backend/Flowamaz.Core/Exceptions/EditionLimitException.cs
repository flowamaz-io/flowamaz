using Flowamaz.Core.Models;

namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a workspace would exceed its edition/plan limit (prompt 05-07). Maps to HTTP 429 with
/// a body carrying the upgrade URL and limit details so the UI can show a targeted upgrade prompt.
/// </summary>
public sealed class EditionLimitException : AppException
{
    public const string UpgradeUrl = "https://flowamaz.com/pricing";

    public LimitType LimitType { get; }
    public long CurrentValue { get; }
    public long LimitValue { get; }

    public EditionLimitException(LimitType limitType, long currentValue, long limitValue, string message)
        : base("PLAN_LIMIT_EXCEEDED", message, httpStatusCode: 429)
    {
        LimitType = limitType;
        CurrentValue = currentValue;
        LimitValue = limitValue;
    }

    /// <summary>snake_case limit identifier for the API body (e.g. "workflow_count").</summary>
    public string LimitTypeKey => LimitType switch
    {
        Models.LimitType.WorkflowCount => "workflow_count",
        Models.LimitType.RunsThisMonth => "runs_this_month",
        Models.LimitType.MemberCount => "member_count",
        _ => "unknown",
    };
}
