namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when creating a workspace whose URL (slug) another workspace in the same organisation already uses.
/// Maps to HTTP 409 Conflict.
/// </summary>
public sealed class WorkspaceSlugTakenException : AppException
{
    public WorkspaceSlugTakenException(string slug)
        : base(
            "WORKSPACE_SLUG_TAKEN",
            $"Your organisation already has a workspace with the URL '{slug}'. Change the workspace URL (or the name) " +
            "and try again, or open the existing workspace from the workspace switcher.",
            httpStatusCode: 409)
    {
    }
}
