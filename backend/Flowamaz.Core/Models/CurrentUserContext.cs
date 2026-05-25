namespace Flowamaz.Core.Models;

/// <summary>
/// The resolved identity of the current request, built by the JWT/API-key middleware and stashed
/// in <c>HttpContext.Items["CurrentUser"]</c>. Read back by the current-user service. Either a
/// human (JWT) or an API key — never both.
/// </summary>
public sealed class CurrentUserContext
{
    /// <summary>Key under which this context is stored in <c>HttpContext.Items</c>.</summary>
    public const string HttpContextItemKey = "CurrentUser";

    public required Guid OrgUserId { get; init; }
    public required Guid OrgId { get; init; }
    public string? OrgSlug { get; init; }
    public string? Email { get; init; }
    public string? Name { get; init; }
    public bool IsOrgOwner { get; init; }

    public bool IsApiKey { get; init; }
    public IReadOnlyList<WorkspaceMembership> WorkspaceMemberships { get; init; } = [];

    // API key path only:
    public Guid? ApiKeyWorkspaceId { get; init; }
    public Guid? ApiKeyEnvironmentId { get; init; }
    public IReadOnlyList<string> ApiKeyScopes { get; init; } = [];
}
