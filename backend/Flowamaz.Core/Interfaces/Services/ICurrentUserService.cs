using Flowamaz.Core.Enums;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Ambient identity of the current request. Read by DbContext to stamp CreatedBy/UpdatedBy on
/// AuditableEntity saves, and by authorisation code. Populated from the JWT or API key by the
/// auth middleware. All members are null/empty when unauthenticated (background jobs, migrations,
/// anonymous endpoints).
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The org user id (JWT <c>sub</c>). Named UserId for DbContext audit stamping.</summary>
    Guid? UserId { get; }
    Guid? OrgId { get; }
    string? OrgSlug { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }

    /// <summary>True when the JWT carries the is_org_owner claim — the org's single owner.</summary>
    bool IsOrgOwner { get; }

    /// <summary>True for a human (JWT) caller. False for an API key or anonymous request.</summary>
    bool IsHumanUser { get; }

    /// <summary>True when the request authenticated with a workspace API key.</summary>
    bool IsApiKey { get; }

    IReadOnlyList<WorkspaceMembership> WorkspaceMemberships { get; }

    // Populated only on the API key path:
    Guid? ApiKeyWorkspaceId { get; }
    Guid? ApiKeyEnvironmentId { get; }
    IReadOnlyList<string> ApiKeyScopes { get; }

    /// <summary>The caller's role in a workspace from the JWT memberships, or null if not a member.</summary>
    WorkspaceRole? GetWorkspaceRole(Guid workspaceId);
}
