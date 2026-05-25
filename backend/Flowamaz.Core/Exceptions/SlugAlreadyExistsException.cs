namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Thrown when registering an organisation with a slug that is already taken. Maps to
/// HTTP 409 Conflict. Message is actionable per CLAUDE.md rule 8.
/// </summary>
public sealed class SlugAlreadyExistsException : AppException
{
    public SlugAlreadyExistsException(string slug)
        : base(
            "ORG_SLUG_TAKEN",
            $"The organisation slug '{slug}' is already in use. Choose a different slug — " +
            "it must be globally unique (lowercase letters, numbers and hyphens).",
            httpStatusCode: 409)
    {
    }
}
