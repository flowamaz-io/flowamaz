namespace Flowamaz.Core.Models;

/// <summary>The quantitative caps that apply at the current edition (prompt 05-07).</summary>
public sealed record EditionLimits(
    string Edition,
    int MaxWorkflows,
    long MaxRunsPerMonth,
    int MaxMembers);

/// <summary>Which limit a check / enforcement targets.</summary>
public enum LimitType
{
    WorkflowCount,
    RunsThisMonth,
    MemberCount,
}

/// <summary>Result of evaluating one limit for a workspace.</summary>
public sealed record LimitCheckResult(
    LimitType LimitType,
    bool LimitReached,
    long CurrentValue,
    long LimitValue,
    string Edition);
