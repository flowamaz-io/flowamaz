using Flowamaz.Core.Git;

namespace Flowamaz.Core.Interfaces.Git;

/// <summary>
/// Git-native workflow versioning (FUNCTIONAL.md §2.6). Each workspace owns a single bare
/// repository (<c>{GIT_REPOS_BASE_PATH}/{workspaceId}/workflows.git</c>) holding one
/// <c>{workflowId}.yaml</c> blob per workflow. Branch = draft, tag = release, commit SHA =
/// immutable instance reference. All operations are workspace-scoped and never touch a working
/// directory — the bare object database is manipulated directly.
/// </summary>
public interface IWorkspaceGitService
{
    /// <summary>
    /// Creates the workspace's bare repo with <c>main</c> as default branch and an initial empty
    /// commit. Idempotent — a no-op if the repo already exists.
    /// </summary>
    Task InitialiseRepoAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Writes <c>{workflowId}.yaml</c> and commits it on the current branch.</summary>
    Task<CommitResult> CommitWorkflowAsync(
        Guid workspaceId,
        Guid workflowId,
        string yamlContent,
        string message,
        string authorName,
        string authorEmail,
        CancellationToken ct = default);

    /// <summary>Tags the current HEAD as <c>{workflowId}/v{version}</c> (lightweight tag).</summary>
    Task<string> PublishWorkflowAsync(Guid workspaceId, Guid workflowId, int version, CancellationToken ct = default);

    /// <summary>Reads <c>{workflowId}.yaml</c> as it existed at <paramref name="commitSha"/>.</summary>
    Task<string> GetWorkflowAtCommitAsync(Guid workspaceId, Guid workflowId, string commitSha, CancellationToken ct = default);

    /// <summary>Unified + node-level diff of the workflow file between two commits.</summary>
    Task<WorkflowDiff> GetDiffAsync(Guid workspaceId, Guid workflowId, string fromSha, string toSha, CancellationToken ct = default);

    /// <summary>Commit history for the workflow file, newest first, capped at <paramref name="limit"/>.</summary>
    Task<List<WorkflowCommit>> GetHistoryAsync(Guid workspaceId, Guid workflowId, int limit, CancellationToken ct = default);

    Task CreateBranchAsync(Guid workspaceId, string branchName, string? fromSha = null, CancellationToken ct = default);

    Task CheckoutBranchAsync(Guid workspaceId, string branchName, CancellationToken ct = default);

    Task<MergeResult> MergeBranchAsync(Guid workspaceId, string sourceBranch, string targetBranch, CancellationToken ct = default);
}
