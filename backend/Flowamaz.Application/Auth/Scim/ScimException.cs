using Flowamaz.Core.Exceptions;

namespace Flowamaz.Application.Auth.Scim;

/// <summary>
/// A SCIM request was refused. Rendered as a SCIM error (RFC 7644 §3.12): <c>status</c>, optional <c>scimType</c>
/// and <c>detail</c>. The detail is read by the IdP admin in their provisioning log, so it says what to change.
/// </summary>
public sealed class ScimException : AppException
{
    public string? ScimType { get; }

    public ScimException(int status, string? scimType, string detail) : base("SCIM_" + (scimType ?? status.ToString()), detail, status)
    {
        ScimType = scimType;
    }

    public static ScimException NotFound(string what) => new(404, null, $"{what} doesn't exist in this Flowamaz organisation.");
    public static ScimException Invalid(string detail) => new(400, "invalidValue", detail);
    public static ScimException Syntax(string detail) => new(400, "invalidSyntax", detail);
    public static ScimException Filter(string detail) => new(400, "invalidFilter", detail);
    public static ScimException Path(string detail) => new(400, "invalidPath", detail);
    public static ScimException Uniqueness(string detail) => new(409, "uniqueness", detail);
    public static ScimException Mutability(string detail) => new(400, "mutability", detail);
}
