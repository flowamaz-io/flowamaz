namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when removing or demoting a member would leave a workspace with no active Admin.
/// Maps to HTTP 409 Conflict. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class LastAdminException : AppException
{
    public LastAdminException()
        : base(
            "WORKSPACE_LAST_ADMIN",
            "This workspace must keep at least one active Admin. Promote another member to Admin " +
            "before removing or demoting the last one.",
            httpStatusCode: 409)
    {
    }
}
