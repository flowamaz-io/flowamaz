using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Models;

/// <summary>Full empathy analysis result for a workflow definition.</summary>
public sealed record EmpathyAnalysis(
    Guid WorkflowDefinitionId,
    int EmailsSentToRequester,
    int WaitPeriodCount,
    int StatusUpdateCount,
    double AvgDaysToOutcome,
    int VisibilityGapHours,
    int Score,
    IReadOnlyList<EmpathyIssue> Issues);

/// <summary>A single empathy issue detected on a workflow node.</summary>
public sealed record EmpathyIssue(
    string NodeId,
    string Label,
    EmpathyIssueType Type,
    string Description,
    string Suggestion,
    string Severity);
