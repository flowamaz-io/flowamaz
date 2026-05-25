namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when adding a user who is already a member of the workspace. Maps to HTTP 409.
/// </summary>
public sealed class AlreadyMemberException : AppException
{
    public AlreadyMemberException()
        : base("WORKSPACE_ALREADY_MEMBER", "Already a member of this workspace. Update their role instead.", httpStatusCode: 409)
    {
    }
}
