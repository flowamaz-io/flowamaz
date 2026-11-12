using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>SCIM persistence. Every query is scoped by organisation (or by a group already resolved within it).</summary>
public sealed class ScimRepository(FlowAmazDbContext db) : IScimRepository
{
    // ── Tokens ──────────────────────────────────────────────────────────────────────────

    public Task<ScimToken?> GetActiveTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.ScimTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAt == null, ct);

    public Task<List<ScimToken>> GetActiveTokensAsync(Guid orgId, CancellationToken ct = default) =>
        db.ScimTokens.Where(t => t.OrgId == orgId && t.RevokedAt == null).OrderBy(t => t.CreatedAt).ToListAsync(ct);

    public Task<ScimToken?> GetTokenAsync(Guid orgId, Guid tokenId, CancellationToken ct = default) =>
        db.ScimTokens.FirstOrDefaultAsync(t => t.OrgId == orgId && t.Id == tokenId, ct);

    public Task<bool> HasActiveTokenAsync(Guid orgId, CancellationToken ct = default) =>
        db.ScimTokens.AnyAsync(t => t.OrgId == orgId && t.RevokedAt == null, ct);

    public async Task AddTokenAsync(ScimToken token, CancellationToken ct = default) =>
        await db.ScimTokens.AddAsync(token, ct);

    public Task TouchTokenAsync(Guid tokenId, DateTime usedAt, CancellationToken ct = default) =>
        db.ScimTokens.Where(t => t.Id == tokenId).ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, usedAt), ct);

    // ── Users ───────────────────────────────────────────────────────────────────────────

    public async Task<(List<OrgUser> Users, int Total)> QueryUsersAsync(
        Guid orgId, ScimUserFilterAttribute? attribute, string? value, int skip, int take, CancellationToken ct = default)
    {
        var query = db.OrgUsers.Where(u => u.OrgId == orgId && u.ScimDeletedAt == null);
        if (attribute is { } a && value is not null)
        {
            var lower = value.ToLowerInvariant();
            query = a switch
            {
                // SCIM userName is case-insensitive (RFC 7643 §4.1.1).
                ScimUserFilterAttribute.UserName => query.Where(u =>
                    (u.ScimUserName != null && u.ScimUserName.ToLower() == lower) || (u.ScimUserName == null && u.Email == lower)),
                ScimUserFilterAttribute.ExternalId => query.Where(u => u.ScimExternalId == value),
                _ => query.Where(u => u.Email == lower),
            };
        }
        var total = await query.CountAsync(ct);
        var users = await query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (users, total);
    }

    public Task<OrgUser?> GetUserAsync(Guid orgId, Guid userId, bool includeDeleted = false, CancellationToken ct = default) =>
        db.OrgUsers.FirstOrDefaultAsync(u => u.OrgId == orgId && u.Id == userId && (includeDeleted || u.ScimDeletedAt == null), ct);

    /// <summary>Includes accounts removed in Flowamaz (soft-deleted): their email is still taken by the unique index.</summary>
    public Task<OrgUser?> GetUserByEmailAsync(Guid orgId, string email, CancellationToken ct = default) =>
        db.OrgUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.OrgId == orgId && u.Email == email, ct);

    public Task<List<OrgUser>> GetUsersAsync(Guid orgId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        db.OrgUsers.Where(u => u.OrgId == orgId && userIds.Contains(u.Id) && u.ScimDeletedAt == null).ToListAsync(ct);

    // ── Groups ──────────────────────────────────────────────────────────────────────────

    public async Task<(List<ScimGroup> Groups, int Total)> QueryGroupsAsync(
        Guid orgId, ScimGroupFilterAttribute? attribute, string? value, int skip, int take, CancellationToken ct = default)
    {
        var query = db.ScimGroups.Where(g => g.OrgId == orgId);
        if (attribute is { } a && value is not null)
            query = a == ScimGroupFilterAttribute.DisplayName
                ? query.Where(g => g.DisplayName == value)
                : query.Where(g => g.ExternalId == value);
        var total = await query.CountAsync(ct);
        var groups = await query.OrderBy(g => g.CreatedAt).ThenBy(g => g.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (groups, total);
    }

    public Task<ScimGroup?> GetGroupAsync(Guid orgId, Guid groupId, CancellationToken ct = default) =>
        db.ScimGroups.FirstOrDefaultAsync(g => g.OrgId == orgId && g.Id == groupId, ct);

    public Task<bool> GroupNameTakenAsync(Guid orgId, string displayName, Guid? exceptGroupId, CancellationToken ct = default) =>
        db.ScimGroups.AnyAsync(g => g.OrgId == orgId && g.DisplayName == displayName && g.Id != exceptGroupId, ct);

    public async Task AddGroupAsync(ScimGroup group, CancellationToken ct = default) => await db.ScimGroups.AddAsync(group, ct);

    public void RemoveGroup(ScimGroup group) => db.ScimGroups.Remove(group);

    // ── Membership ──────────────────────────────────────────────────────────────────────

    public Task<List<ScimMemberRef>> GetMembersAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct = default) =>
        (from m in db.ScimGroupMembers
         join u in db.OrgUsers on m.OrgUserId equals u.Id
         where groupIds.Contains(m.GroupId) && u.ScimDeletedAt == null
         orderby u.Email
         select new ScimMemberRef(m.GroupId, u.Id, u.Email)).ToListAsync(ct);

    public Task<List<ScimGroupMember>> GetMembershipRowsAsync(Guid groupId, CancellationToken ct = default) =>
        db.ScimGroupMembers.Where(m => m.GroupId == groupId).ToListAsync(ct);

    public Task<List<ScimGroupMember>> GetMembershipRowsForUserAsync(Guid orgId, Guid userId, CancellationToken ct = default) =>
        (from m in db.ScimGroupMembers
         join g in db.ScimGroups on m.GroupId equals g.Id
         where g.OrgId == orgId && m.OrgUserId == userId
         select m).ToListAsync(ct);

    public async Task AddMemberAsync(ScimGroupMember member, CancellationToken ct = default) =>
        await db.ScimGroupMembers.AddAsync(member, ct);

    public void RemoveMember(ScimGroupMember member) => db.ScimGroupMembers.Remove(member);

    public Task<List<string>> GetGroupNamesForUserAsync(Guid orgId, Guid userId, CancellationToken ct = default) =>
        (from m in db.ScimGroupMembers
         join g in db.ScimGroups on m.GroupId equals g.Id
         where g.OrgId == orgId && m.OrgUserId == userId
         select g.DisplayName).ToListAsync(ct);

    // ── Background role re-sync ─────────────────────────────────────────────────────────

    public async Task QueueRoleResyncAsync(Guid orgId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return;
        var ids = userIds.Distinct().ToArray();
        var now = DateTime.UtcNow;
        // Only people of this organisation are queued, whatever ids are passed.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO scim_role_resyncs (id, org_id, org_user_id, created_at, updated_at, is_deleted, attempts)
            SELECT gen_random_uuid(), {orgId}, u.id, {now}, {now}, false, 0
            FROM org_users u
            WHERE u.org_id = {orgId} AND u.id = ANY({ids})
            ON CONFLICT (org_id, org_user_id) DO UPDATE SET updated_at = clock_timestamp(), attempts = 0
            """, ct);
    }

    public Task<List<ScimRoleResync>> ClaimRoleResyncsAsync(int take, int maxAttempts, CancellationToken ct = default) =>
        db.ScimRoleResyncs
            .FromSqlInterpolated($"SELECT * FROM scim_role_resyncs WHERE attempts < {maxAttempts} ORDER BY attempts, created_at LIMIT {take} FOR UPDATE SKIP LOCKED")
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task DeleteRoleResyncsAsync(IReadOnlyCollection<ScimRoleResync> claimed, CancellationToken ct = default)
    {
        if (claimed.Count == 0) return;
        var ids = claimed.Select(c => c.Id).ToArray();
        var stamps = claimed.Select(c => DateTime.SpecifyKind(c.UpdatedAt, DateTimeKind.Utc)).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM scim_role_resyncs r
            USING unnest({ids}, {stamps}) AS c(id, stamp)
            WHERE r.id = c.id AND r.updated_at = c.stamp
            """, ct);
    }

    public async Task MarkRoleResyncsFailedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        var array = ids.ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE scim_role_resyncs SET attempts = attempts + 1, updated_at = clock_timestamp() WHERE id = ANY({array})", ct);
    }

    public Task<int> CountQueuedRoleResyncsAsync(Guid orgId, CancellationToken ct = default) =>
        db.ScimRoleResyncs.CountAsync(r => r.OrgId == orgId, ct);
}
