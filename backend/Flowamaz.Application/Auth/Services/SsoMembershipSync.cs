using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Auth.Services;

/// <summary>
/// Applies the organisation's SSO group → role mappings to one person's workspace memberships. Used at SSO sign-in
/// (groups from the assertion, or from SCIM) and whenever SCIM changes a person's groups. Call inside a transaction:
/// demoting an Admin takes the workspace's admin lock.
/// </summary>
public sealed class SsoMembershipSync
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IWorkspaceMemberRepository _members;
    private readonly ILogger _logger;

    public SsoMembershipSync(IWorkspaceRepository workspaces, IWorkspaceMemberRepository members, ILogger logger)
    {
        _workspaces = workspaces;
        _members = members;
        _logger = logger;
    }

    /// <summary>
    /// Brings the user's SSO-managed memberships in line with their IdP groups. For each workspace in scope (named by
    /// a mapping, covered by an all-workspaces mapping, or a default workspace) the wanted role is the highest
    /// matching mapping, else <c>DefaultRole</c> for default workspaces, else none.
    /// <list type="bullet">
    /// <item>No membership yet and a role is wanted → added, marked <see cref="WorkspaceMember.ManagedBySso"/>.</item>
    /// <item>SSO-managed membership → role updated, or deactivated when no role is wanted (and re-activated later).</item>
    /// <item>Memberships added or edited by hand, or deactivated by an admin, are never touched.</item>
    /// <item>A workspace's last active Admin is kept, checked under a per-workspace lock.</item>
    /// </list>
    /// </summary>
    public async Task SyncAsync(OrgUser user, OrgSsoConfig config, IReadOnlyList<string> groups, bool isNew, CancellationToken ct)
    {
        if (config.GroupRoleMappings.Count == 0 && config.DefaultWorkspaceIds.Count == 0) return;

        var groupSet = groups.ToHashSet(StringComparer.Ordinal);
        var coversAll = config.GroupRoleMappings.Any(m => m.WorkspaceId is null);
        var named = config.GroupRoleMappings.Where(m => m.WorkspaceId is not null).Select(m => m.WorkspaceId!.Value)
            .Concat(config.DefaultWorkspaceIds).ToHashSet();

        foreach (var workspace in (await _workspaces.GetForOrgAsync(user.OrgId, ct)).OrderBy(w => w.Id))
        {
            if (!coversAll && !named.Contains(workspace.Id)) continue;

            var matched = config.GroupRoleMappings
                .Where(m => groupSet.Contains(m.Group) && (m.WorkspaceId is null || m.WorkspaceId == workspace.Id))
                .Select(m => (WorkspaceRole?)m.Role)
                .Max();
            var wanted = matched ?? (config.DefaultWorkspaceIds.Contains(workspace.Id) ? config.DefaultRole : null);

            var member = isNew ? null : await _members.GetAsync(workspace.Id, user.Id, ct);
            if (member is null)
            {
                if (wanted is { } role)
                    await _members.AddAsync(new WorkspaceMember
                    {
                        WorkspaceId = workspace.Id, OrgUserId = user.Id, Role = role, IsActive = true, ManagedBySso = true,
                    }, ct);
                continue;
            }
            if (!member.ManagedBySso) continue; // added or edited by hand, or deactivated by an admin

            var (newRole, newActive) = wanted is { } w ? (w, true) : (member.Role, false);
            if (member.Role == newRole && member.IsActive == newActive) continue;

            var losesAdmin = member is { IsActive: true, Role: WorkspaceRole.Admin } && (!newActive || newRole != WorkspaceRole.Admin);
            if (losesAdmin)
            {
                await _members.LockAdminsAsync(workspace.Id, ct);
                if (await _members.CountActiveAdminsAsync(workspace.Id, ct) <= 1)
                {
                    _logger.LogWarning("SSO sync kept last Admin of workspace={WorkspaceId} user={UserId} (IdP groups would demote)", workspace.Id, user.Id);
                    continue;
                }
            }
            member.Role = newRole;
            member.IsActive = newActive;
            _members.Update(member);
        }
    }
}
