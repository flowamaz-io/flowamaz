using Flowamaz.Core.Git;

namespace Flowamaz.Core.Interfaces.Runtime;

// Plug-in points between workflow management (WorkflowService: create, save, publish, archive) and the features a
// host adds around it (docs/ENGINE-RUNTIME-SPLIT.md §2). Each has a default that needs nothing else; the Flowamaz
// product registers its own (Git versioning, edition limits, audit log) in Application/Workflow/Hosting.

/// <summary>
/// Where saved and published workflow YAML is versioned. The YAML itself is always kept in the database (the
/// definition's draft and one <c>WorkflowVersion</c> row per publish); this store adds history on top and returns
/// the reference recorded as the definition's <c>CurrentVersion</c> and the version's <c>CommitSha</c>.
/// </summary>
public interface IWorkflowVersionStore
{
    /// <summary>Records one save (create, update or publish). Null means "no reference": the caller keeps its own.</summary>
    Task<WorkflowSaveResult?> SaveAsync(WorkflowSave save, CancellationToken ct);

    /// <summary>Marks the latest save as release <paramref name="versionNumber"/> (1, 2, …) and returns its tag, if any.</summary>
    Task<string?> TagReleaseAsync(Guid workspaceId, Guid workflowId, int versionNumber, CancellationToken ct);
}

/// <param name="Message">Describes the save, e.g. "Update Expenses", with any Co-authored-by trailers.</param>
/// <param name="Author">Who saved; null for the system (creation, publish).</param>
public sealed record WorkflowSave(Guid WorkspaceId, Guid WorkflowId, string YamlContent, string Message, CommitAuthor? Author);

/// <param name="VersionRef">An immutable reference to this save (a Git commit SHA in the product); at most 64 characters.</param>
public sealed record WorkflowSaveResult(string VersionRef, string BranchName);

/// <summary>Decides whether a workspace may add a workflow (create, or unarchive). Throw an actionable exception to refuse.</summary>
public interface IWorkflowAdmission
{
    Task EnsureCanAddAsync(Guid workspaceId, CancellationToken ct);
}

/// <summary>Receives workflow lifecycle events (audit, notifications). Must not throw for business reasons.</summary>
public interface IWorkflowEventSink
{
    Task WorkflowChangedAsync(WorkflowChangedEvent e, CancellationToken ct);
}

/// <param name="Action">created, deleted, archived, unarchived or published.</param>
/// <param name="VersionNumber">For published: the release number.</param>
/// <param name="VersionRef">For published: the version's reference.</param>
public sealed record WorkflowChangedEvent(
    Guid WorkspaceId, Guid WorkflowId, string WorkflowName, string Action, Guid ActorUserId,
    int? VersionNumber = null, string? VersionRef = null);

/// <summary>
/// Default version store: no history beyond the database. Each save gets a new opaque reference and each
/// release the tag <c>v{n}</c> (tags are per workflow); the published YAML is the version row's own copy.
/// </summary>
public sealed class DatabaseWorkflowVersionStore : IWorkflowVersionStore
{
    public static readonly DatabaseWorkflowVersionStore Instance = new();

    public Task<WorkflowSaveResult?> SaveAsync(WorkflowSave save, CancellationToken ct) =>
        Task.FromResult<WorkflowSaveResult?>(new WorkflowSaveResult(Guid.NewGuid().ToString("N"), "main"));

    public Task<string?> TagReleaseAsync(Guid workspaceId, Guid workflowId, int versionNumber, CancellationToken ct) =>
        Task.FromResult<string?>($"v{versionNumber}");
}

/// <summary>Default admission: every workflow may be added.</summary>
public sealed class AllowAllWorkflowAdmission : IWorkflowAdmission
{
    public static readonly AllowAllWorkflowAdmission Instance = new();
    public Task EnsureCanAddAsync(Guid workspaceId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Default sink: ignores events.</summary>
public sealed class NullWorkflowEventSink : IWorkflowEventSink
{
    public static readonly NullWorkflowEventSink Instance = new();
    public Task WorkflowChangedAsync(WorkflowChangedEvent e, CancellationToken ct) => Task.CompletedTask;
}
