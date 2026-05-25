namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when adding a workspace member by an email that has no user in the caller's org.
/// Maps to HTTP 404. Message tells the admin the person must register first (CLAUDE.md rule 8).
/// </summary>
public sealed class UserNotInOrganisationException : AppException
{
    public UserNotInOrganisationException()
        : base(
            "USER_NOT_IN_ORG",
            "No user with that email exists in your organisation. They must register first.",
            httpStatusCode: 404)
    {
    }
}
