namespace Flowamaz.Core.Models;

// Read models the runtime's event repository returns for run analytics (used by Workflow DNA and Empathy).

/// <summary>Finished, non-test runs of one workflow since a date: the basis for comparing workflows (§8.8).</summary>
public sealed record RunPerformanceRow(int Completed, int Failed, double AvgDurationMs, int SlaBreaches);

/// <summary>One finished run, as the empathy analysis needs it.</summary>
public sealed record EmpathyRunTimeline(
    Guid InstanceId,
    DateTime StartedAt,
    DateTime EndedAt,
    /// <summary>When each node completed (NodeCompleted events), by node id.</summary>
    IReadOnlyList<(string NodeId, DateTime At)> CompletedNodes,
    /// <summary>Approval steps: opened and decided (decided may be missing when the run ended first).</summary>
    IReadOnlyList<(string NodeId, DateTime OpenedAt, DateTime? DecidedAt)> Gates);
