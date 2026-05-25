namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a user attempts to change their own role or remove themselves from a workspace.
/// Maps to HTTP 409 Conflict. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class SelfModificationException : AppException
{
    public SelfModificationException(string action)
        : base(
            "WORKSPACE_SELF_MODIFICATION",
            $"You cannot {action} yourself. Ask another Workspace Admin to make this change.",
            httpStatusCode: 409)
    {
    }
}
