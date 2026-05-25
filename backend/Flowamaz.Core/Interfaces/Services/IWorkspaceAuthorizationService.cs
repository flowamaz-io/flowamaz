using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// The workspace authorization gate. Resolves a member's role and enforces minimum-role and
/// permission checks (FUNCTIONAL.md §4.5). Every workspace-scoped action funnels through here.
/// </summary>
public interface IWorkspaceAuthorizationService
{
    /// <summary>Throws <see cref="Exceptions.InsufficientRoleException"/> when the member's role is below the minimum.</summary>
    Task RequireMinimumRoleAsync(Guid workspaceId, Guid orgUserId, WorkspaceRole minimumRole, CancellationToken cancellationToken = default);

    /// <summary>True when the member's role meets the minimum mapped to <paramref name="permission"/>.</summary>
    Task<bool> HasPermissionAsync(Guid workspaceId, Guid orgUserId, string permission, CancellationToken cancellationToken = default);

    Task<bool> IsWorkspaceAdminAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken = default);

    /// <summary>True when the granted scopes include <paramref name="requiredScope"/>.</summary>
    bool ValidateApiKeyScope(IReadOnlyCollection<string> scopes, string requiredScope);
}
