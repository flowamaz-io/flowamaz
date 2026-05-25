using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workspace.Services;

/// <summary>
/// The workspace authorization gate (FUNCTIONAL.md §4.5). A member's effective role is their role
/// only while active — an inactive member has no role and fails every check. The permission→role
/// map is seeded in the constructor (not a DB table) and unknown permissions are denied by default.
/// </summary>
public sealed class WorkspaceAuthorizationService : IWorkspaceAuthorizationService
{
    private readonly IWorkspaceMemberRepository _memberRepository;
    private readonly ILogger<WorkspaceAuthorizationService> _logger;
    private readonly IReadOnlyDictionary<string, WorkspaceRole> _permissionMinimums;

    public WorkspaceAuthorizationService(
        IWorkspaceMemberRepository memberRepository,
        ILogger<WorkspaceAuthorizationService> logger)
    {
        _memberRepository = memberRepository;
        _logger = logger;
        _permissionMinimums = BuildPermissionMap();
    }

    public async Task RequireMinimumRoleAsync(
        Guid workspaceId, Guid orgUserId, WorkspaceRole minimumRole, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkspaceAuthorizationService.RequireMinimumRoleAsync enter workspaceId={WorkspaceId} orgUserId={OrgUserId} minimumRole={MinimumRole}",
            workspaceId, orgUserId, minimumRole);
        try
        {
            var role = await GetEffectiveRoleAsync(workspaceId, orgUserId, cancellationToken);
            if (role is null || (int)role.Value < (int)minimumRole)
            {
                throw new InsufficientRoleException(minimumRole, role);
            }

            _logger.LogDebug(
                "WorkspaceAuthorizationService.RequireMinimumRoleAsync exit workspaceId={WorkspaceId} orgUserId={OrgUserId} role={Role} granted=true",
                workspaceId, orgUserId, role);
        }
        catch (Exception ex) when (ex is not InsufficientRoleException)
        {
            _logger.LogError(ex,
                "WorkspaceAuthorizationService.RequireMinimumRoleAsync error workspaceId={WorkspaceId} orgUserId={OrgUserId}", workspaceId, orgUserId);
            throw;
        }
    }

    public async Task<bool> HasPermissionAsync(
        Guid workspaceId, Guid orgUserId, string permission, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkspaceAuthorizationService.HasPermissionAsync enter workspaceId={WorkspaceId} orgUserId={OrgUserId} permission={Permission}",
            workspaceId, orgUserId, permission);
        try
        {
            if (!_permissionMinimums.TryGetValue(permission, out var minimumRole))
            {
                _logger.LogWarning(
                    "WorkspaceAuthorizationService.HasPermissionAsync unknown permission={Permission} — denied by default", permission);
                return false;
            }

            var role = await GetEffectiveRoleAsync(workspaceId, orgUserId, cancellationToken);
            var granted = role is not null && (int)role.Value >= (int)minimumRole;
            _logger.LogDebug(
                "WorkspaceAuthorizationService.HasPermissionAsync exit workspaceId={WorkspaceId} orgUserId={OrgUserId} permission={Permission} granted={Granted}",
                workspaceId, orgUserId, permission, granted);
            return granted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WorkspaceAuthorizationService.HasPermissionAsync error workspaceId={WorkspaceId} orgUserId={OrgUserId} permission={Permission}",
                workspaceId, orgUserId, permission);
            throw;
        }
    }

    public async Task<bool> IsWorkspaceAdminAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkspaceAuthorizationService.IsWorkspaceAdminAsync enter workspaceId={WorkspaceId} orgUserId={OrgUserId}", workspaceId, orgUserId);
        try
        {
            var role = await GetEffectiveRoleAsync(workspaceId, orgUserId, cancellationToken);
            var isAdmin = role == WorkspaceRole.Admin;
            _logger.LogDebug(
                "WorkspaceAuthorizationService.IsWorkspaceAdminAsync exit workspaceId={WorkspaceId} orgUserId={OrgUserId} isAdmin={IsAdmin}",
                workspaceId, orgUserId, isAdmin);
            return isAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WorkspaceAuthorizationService.IsWorkspaceAdminAsync error workspaceId={WorkspaceId} orgUserId={OrgUserId}", workspaceId, orgUserId);
            throw;
        }
    }

    public bool ValidateApiKeyScope(IReadOnlyCollection<string> scopes, string requiredScope)
    {
        var granted = scopes.Contains(requiredScope);
        _logger.LogDebug(
            "WorkspaceAuthorizationService.ValidateApiKeyScope requiredScope={RequiredScope} granted={Granted}", requiredScope, granted);
        return granted;
    }

    private async Task<WorkspaceRole?> GetEffectiveRoleAsync(Guid workspaceId, Guid orgUserId, CancellationToken cancellationToken)
    {
        var member = await _memberRepository.GetAsync(workspaceId, orgUserId, cancellationToken);
        return member is { IsActive: true } ? member.Role : null;
    }

    // Minimum role required per permission (FUNCTIONAL.md §4.5). Seeded once at construction.
    private static IReadOnlyDictionary<string, WorkspaceRole> BuildPermissionMap() => new Dictionary<string, WorkspaceRole>
    {
        ["workflows.read"] = WorkspaceRole.Viewer,
        ["workflows.create"] = WorkspaceRole.Designer,
        ["workflows.update"] = WorkspaceRole.Designer,
        ["workflows.publish.dev"] = WorkspaceRole.Designer,
        ["workflows.publish.staging"] = WorkspaceRole.Designer,
        ["workflows.publish.production"] = WorkspaceRole.Admin,
        ["instances.read"] = WorkspaceRole.Viewer,
        ["instances.trigger"] = WorkspaceRole.Operator,
        ["instances.cancel"] = WorkspaceRole.Operator,
        ["instances.retry"] = WorkspaceRole.Operator,
        ["instances.variables.read"] = WorkspaceRole.Operator,
        ["credentials.manage"] = WorkspaceRole.Admin,
        ["connectors.configure"] = WorkspaceRole.Admin,
        ["members.manage"] = WorkspaceRole.Admin,
        ["workspace.settings"] = WorkspaceRole.Admin,
        ["audit.read"] = WorkspaceRole.Operator,
        ["gates.decide"] = WorkspaceRole.Operator,
    };
}
