namespace Flowamaz.Core.Git;

/// <summary>Result of committing workflow YAML to a workspace's bare Git repository.</summary>
public sealed record CommitResult(string CommitSha, string BranchName, string? TagName);

/// <summary>A single commit in a workflow file's history.</summary>
public sealed record WorkflowCommit(
    string Sha,
    string ShortSha,
    string Message,
    string AuthorName,
    string AuthorEmail,
    DateTime CommittedAt);

/// <summary>
/// A unified diff of a workflow file between two commits. <see cref="Lines"/> drives the
/// side-by-side colour-coded view; <see cref="Summary"/> drives the node-level header.
/// </summary>
public sealed record WorkflowDiff(
    string FromSha,
    string ToSha,
    IReadOnlyList<WorkflowDiffLine> Lines,
    WorkflowDiffSummary Summary);

/// <summary>One line of a unified diff. <see cref="Type"/> is "added", "removed", or "context".</summary>
public sealed record WorkflowDiffLine(
    string Type,
    string Content,
    int? OldLineNumber,
    int? NewLineNumber);

/// <summary>Node-level change counts derived by comparing the two YAML versions.</summary>
public sealed record WorkflowDiffSummary(
    IReadOnlyList<string> AddedNodes,
    IReadOnlyList<string> RemovedNodes,
    IReadOnlyList<string> ModifiedNodes);

/// <summary>Outcome of merging one branch into another.</summary>
public sealed record MergeResult(
    bool Success,
    string? CommitSha,
    bool HasConflicts,
    IReadOnlyList<string> ConflictPaths);
