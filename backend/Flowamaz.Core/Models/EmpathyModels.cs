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
    IReadOnlyList<EmpathyIssue> Issues,
    /// <summary>What real runs of the last 30 days showed; null when there were none to analyse.</summary>
    EmpathyRunEvidence? Runs = null);

/// <summary>
/// The requester's experience measured from finished, non-test runs (last 30 days, most recent 200): how long they
/// waited, how many messages the workflow sent, and how long it went quiet (FUNCTIONAL.md §8.2).
/// </summary>
public sealed record EmpathyRunEvidence(
    int RunsAnalysed,
    double AvgDaysToOutcome,
    double AvgMessagesPerRun,
    /// <summary>Median over runs of the longest stretch without any message from the workflow.</summary>
    double MedianLongestSilenceHours,
    /// <summary>Share of each run spent waiting for approvals, on average (0–100).</summary>
    int PercentTimeWaitingOnApprovals,
    /// <summary>Share of runs where a message went out after the last approval decision (0–100).</summary>
    int PercentRunsOutcomeNotified,
    /// <summary>The approval step that kept people waiting longest on average, if any.</summary>
    string? SlowestGateNodeId,
    double SlowestGateAvgHours);

/// <summary>One finished run, as the empathy analysis needs it.</summary>
public sealed record EmpathyRunTimeline(
    Guid InstanceId,
    DateTime StartedAt,
    DateTime EndedAt,
    /// <summary>When each node completed (NodeCompleted events), by node id.</summary>
    IReadOnlyList<(string NodeId, DateTime At)> CompletedNodes,
    /// <summary>Approval steps: opened and decided (decided may be missing when the run ended first).</summary>
    IReadOnlyList<(string NodeId, DateTime OpenedAt, DateTime? DecidedAt)> Gates);

/// <summary>A single empathy issue detected on a workflow node.</summary>
public sealed record EmpathyIssue(
    string NodeId,
    string Label,
    EmpathyIssueType Type,
    string Description,
    string Suggestion,
    string Severity);
