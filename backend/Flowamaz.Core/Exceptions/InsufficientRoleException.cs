using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when a member's workspace role is below the minimum required for an action.
/// Maps to HTTP 403 Forbidden. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class InsufficientRoleException : AppException
{
    public WorkspaceRole RequiredRole { get; }
    public WorkspaceRole? ActualRole { get; }

    public InsufficientRoleException(WorkspaceRole requiredRole, WorkspaceRole? actualRole)
        : base(
            "WORKSPACE_INSUFFICIENT_ROLE",
            $"This action requires the '{requiredRole}' role or higher. " +
            $"Your role in this workspace is '{(actualRole?.ToString() ?? "none — you are not a member")}'. " +
            "Ask a Workspace Admin to grant you a higher role.",
            httpStatusCode: 403)
    {
        RequiredRole = requiredRole;
        ActualRole = actualRole;
    }
}
