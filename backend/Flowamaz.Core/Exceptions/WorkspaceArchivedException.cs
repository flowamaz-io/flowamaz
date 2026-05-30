namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a workflow trigger targets an archived workspace. Archiving preserves all data but
/// disables new runs. Maps to HTTP 409 Conflict. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class WorkspaceArchivedException : AppException
{
    public WorkspaceArchivedException()
        : base(
            "WORKSPACE_ARCHIVED",
            "This workspace is archived, so new runs are disabled. Restore the workspace from its " +
            "settings to trigger workflows again.",
            httpStatusCode: 409)
    {
    }
}
