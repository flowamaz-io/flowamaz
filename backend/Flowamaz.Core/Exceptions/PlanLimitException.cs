namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a workspace has exhausted its monthly AI token budget (hard cap).
/// Maps to HTTP 429. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class PlanLimitException : AppException
{
    public PlanLimitException(string resource)
        : base(
            "PLAN_LIMIT_EXCEEDED",
            $"Monthly AI token budget exceeded for {resource}. Upgrade your plan or increase your AI budget in workspace settings.",
            httpStatusCode: 429)
    {
    }
}
