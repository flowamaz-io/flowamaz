namespace Flowamaz.Core.Models;

/// <summary>
/// A user's active membership in one workspace, as embedded in the JWT (workspace_id,
/// workspace_slug, role_name, role_value) and surfaced by the current-user context.
/// <see cref="WorkspaceName"/> is carried for the /me view but omitted from the token.
/// </summary>
public sealed record WorkspaceMembership(
    Guid WorkspaceId,
    string WorkspaceSlug,
    string WorkspaceName,
    string RoleName,
    int RoleValue);
