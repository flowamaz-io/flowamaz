using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>The SCIM attribute a list request filters on (only <c>eq</c> filters are supported).</summary>
public enum ScimUserFilterAttribute { UserName, ExternalId, Email }

public enum ScimGroupFilterAttribute { DisplayName, ExternalId }

/// <summary>A group member with what the SCIM response shows for it.</summary>
public sealed record ScimMemberRef(Guid GroupId, Guid OrgUserId, string Email);

/// <summary>
/// Persistence for SCIM provisioning. Everything is scoped by organisation: a token only ever sees its own org's
/// users and groups. The caller commits via <see cref="Persistence.IUnitOfWork"/>.
/// </summary>
public interface IScimRepository
{
    // Tokens
    Task<ScimToken?> GetActiveTokenByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<List<ScimToken>> GetActiveTokensAsync(Guid orgId, CancellationToken ct = default);
    Task<ScimToken?> GetTokenAsync(Guid orgId, Guid tokenId, CancellationToken ct = default);
    Task<bool> HasActiveTokenAsync(Guid orgId, CancellationToken ct = default);
    Task AddTokenAsync(ScimToken token, CancellationToken ct = default);
    Task TouchTokenAsync(Guid tokenId, DateTime usedAt, CancellationToken ct = default);

    // Users (org users not deleted over SCIM)
    Task<(List<OrgUser> Users, int Total)> QueryUsersAsync(
        Guid orgId, ScimUserFilterAttribute? attribute, string? value, int skip, int take, CancellationToken ct = default);
    Task<OrgUser?> GetUserAsync(Guid orgId, Guid userId, bool includeDeleted = false, CancellationToken ct = default);
    /// <summary>Any account with this email in the org, including ones removed in Flowamaz (check <c>IsDeleted</c>).</summary>
    Task<OrgUser?> GetUserByEmailAsync(Guid orgId, string email, CancellationToken ct = default);
    Task<List<OrgUser>> GetUsersAsync(Guid orgId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    // Groups
    Task<(List<ScimGroup> Groups, int Total)> QueryGroupsAsync(
        Guid orgId, ScimGroupFilterAttribute? attribute, string? value, int skip, int take, CancellationToken ct = default);
    Task<ScimGroup?> GetGroupAsync(Guid orgId, Guid groupId, CancellationToken ct = default);
    Task<bool> GroupNameTakenAsync(Guid orgId, string displayName, Guid? exceptGroupId, CancellationToken ct = default);
    Task AddGroupAsync(ScimGroup group, CancellationToken ct = default);
    void RemoveGroup(ScimGroup group);

    // Membership
    Task<List<ScimMemberRef>> GetMembersAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct = default);
    Task<List<ScimGroupMember>> GetMembershipRowsAsync(Guid groupId, CancellationToken ct = default);
    Task<List<ScimGroupMember>> GetMembershipRowsForUserAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task AddMemberAsync(ScimGroupMember member, CancellationToken ct = default);
    void RemoveMember(ScimGroupMember member);
    Task<List<string>> GetGroupNamesForUserAsync(Guid orgId, Guid userId, CancellationToken ct = default);
}
