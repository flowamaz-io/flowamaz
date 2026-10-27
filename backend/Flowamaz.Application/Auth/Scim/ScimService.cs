using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowamaz.Application.Auth.Services;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Auth.Scim;

/// <summary>Who is calling the SCIM API: one organisation, through one of its tokens.</summary>
public sealed record ScimCaller(Guid OrgId, Guid TokenId);

/// <summary>A SCIM token as the settings page shows it (never the token itself).</summary>
public sealed record ScimTokenDto(Guid Id, string Name, string TokenPrefix, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>A newly created token: the plain value is returned this once.</summary>
public sealed record ScimTokenCreated(ScimTokenDto Token, string PlainToken, string ScimBaseUrl);

/// <summary>
/// SCIM 2.0 provisioning (RFC 7643/7644) for Enterprise organisations, so the IdP creates, updates and removes
/// people and their groups in Flowamaz.
/// <list type="bullet">
/// <item>A token belongs to one organisation; everything it reads or changes is in that organisation.</item>
/// <item>Users: created without a password (they sign in with SSO). Deactivating or deleting one ends their sessions;
/// deleting keeps the account deactivated and reuses it if the IdP creates the person again.</item>
/// <item>Organisation owners can't be deactivated, deleted or have their email changed over SCIM: an IdP admin must not
/// be able to take over the break-glass owner account (an email change would let them reset its password).</item>
/// <item>Groups: their display names feed the SSO group → role mappings. When SCIM changes someone's groups, their
/// SSO-managed workspace memberships are updated straight away (same rules as at sign-in).</item>
/// </list>
/// </summary>
public sealed class ScimService
{
    public const string TokenPrefix = "fmzscim_";
    public const int MaxActiveTokens = 5;

    private readonly IScimRepository _scim;
    private readonly IOrganisationRepository _orgs;
    private readonly IPlanRepository _plans;
    private readonly IOrgUserRepository _orgUsers;
    private readonly IOrgSsoConfigRepository _configs;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScimService> _logger;
    private readonly IAuditService? _audit;
    private readonly SsoMembershipSync _sync;

    public ScimService(
        IScimRepository scim,
        IOrganisationRepository orgs,
        IPlanRepository plans,
        IOrgUserRepository orgUsers,
        IOrgSsoConfigRepository configs,
        IRefreshTokenRepository refreshTokens,
        IWorkspaceRepository workspaces,
        IWorkspaceMemberRepository members,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<ScimService> logger,
        IAuditService? audit = null)
    {
        _scim = scim;
        _orgs = orgs;
        _plans = plans;
        _orgUsers = orgUsers;
        _configs = configs;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _logger = logger;
        _audit = audit;
        _sync = new SsoMembershipSync(workspaces, members, logger);
    }

    public string ScimBaseUrl
    {
        get
        {
            var configured = _configuration["Platform:BaseUrl"];
            var app = string.IsNullOrWhiteSpace(configured) ? "https://app.flowamaz.io" : configured.TrimEnd('/');
            return $"{app}/api/scim/v2";
        }
    }

    // ── Authentication ──────────────────────────────────────────────────────────────────

    /// <summary>The organisation behind a bearer token, or null (unknown, revoked, or the plan has no SSO).</summary>
    public async Task<ScimCaller?> AuthenticateAsync(string? bearerToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(bearerToken) || !bearerToken.StartsWith(TokenPrefix, StringComparison.Ordinal) || bearerToken.Length > 200)
            return null;
        var token = await _scim.GetActiveTokenByHashAsync(Hash(bearerToken), ct);
        if (token is null) return null;
        var org = await _orgs.GetByIdAsync(token.OrgId, ct);
        if (org is null || !((await _plans.GetByIdAsync(org.PlanId, ct))?.Features.Sso ?? false))
        {
            _logger.LogWarning("ScimService.AuthenticateAsync refused token={TokenId}: organisation not on a plan with SSO", token.Id);
            return null;
        }
        var now = DateTime.UtcNow;
        if (token.LastUsedAt is null || now - token.LastUsedAt > TimeSpan.FromMinutes(1))
            await _scim.TouchTokenAsync(token.Id, now, ct);
        return new ScimCaller(token.OrgId, token.Id);
    }

    // ── Tokens (org owners) ─────────────────────────────────────────────────────────────

    public async Task<List<ScimTokenDto>> ListTokensAsync(Guid orgId, CancellationToken ct = default) =>
        (await _scim.GetActiveTokensAsync(orgId, ct)).Select(ToDto).ToList();

    public async Task<ScimTokenCreated> CreateTokenAsync(Guid orgId, string? name, Guid createdBy, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.CreateTokenAsync enter org={OrgId}", orgId);
        var org = await _orgs.GetByIdAsync(orgId, ct) ?? throw SsoLoginException.NotOnPlan();
        if (!((await _plans.GetByIdAsync(org.PlanId, ct))?.Features.Sso ?? false)) throw SsoLoginException.NotOnPlan();
        var label = string.IsNullOrWhiteSpace(name) ? "SCIM token" : name.Trim();
        if (label.Length > 100) throw new SsoLoginException("SCIM_TOKEN_INVALID", "Give the token a name of up to 100 characters.", 400);
        if ((await _scim.GetActiveTokensAsync(orgId, ct)).Count >= MaxActiveTokens)
            throw new SsoLoginException("SCIM_TOKEN_LIMIT",
                $"An organisation can have {MaxActiveTokens} SCIM tokens. Revoke one you no longer use, then create a new one.", 400);

        var plain = TokenPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = new ScimToken
        {
            OrgId = orgId, Name = label, TokenHash = Hash(plain), TokenPrefix = plain[..14], CreatedBy = createdBy,
        };
        await _scim.AddTokenAsync(token, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        Audit(orgId, createdBy, "scim.token_created", "created", token.Id, new { name = label }, ct);
        _logger.LogInformation("ScimService.CreateTokenAsync exit org={OrgId} token={TokenId}", orgId, token.Id);
        return new ScimTokenCreated(ToDto(token), plain, ScimBaseUrl);
    }

    public async Task RevokeTokenAsync(Guid orgId, Guid tokenId, Guid revokedBy, CancellationToken ct = default)
    {
        var token = await _scim.GetTokenAsync(orgId, tokenId, ct)
            ?? throw new SsoLoginException("SCIM_TOKEN_NOT_FOUND", "That SCIM token doesn't exist. Refresh the page to see the current tokens.", 404);
        if (token.RevokedAt is not null) return;
        token.RevokedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        Audit(orgId, revokedBy, "scim.token_revoked", "revoked", token.Id, new { name = token.Name }, ct);
    }

    // ── Users ───────────────────────────────────────────────────────────────────────────

    public async Task<JsonObject> ListUsersAsync(Guid orgId, string? filter, int? startIndex, int? count, CancellationToken ct = default)
    {
        var parsed = ScimJson.UserFilter(filter);
        var (skip, take, start) = Page(startIndex, count);
        var (users, total) = await _scim.QueryUsersAsync(orgId, parsed?.Item1, parsed?.Item2, skip, take, ct);
        return ScimJson.List(users.Select(u => (JsonNode)ScimJson.User(u, ScimBaseUrl)), total, start);
    }

    public async Task<JsonObject> GetUserAsync(Guid orgId, Guid id, CancellationToken ct = default) =>
        ScimJson.User(await _scim.GetUserAsync(orgId, id, ct: ct) ?? throw ScimException.NotFound("This user"), ScimBaseUrl);

    public async Task<JsonObject> CreateUserAsync(Guid orgId, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.CreateUserAsync enter org={OrgId}", orgId);
        var input = UserInput.From(ScimJson.Parse(body), requireUserName: true);
        var email = input.Email ?? input.UserNameEmail ?? throw ScimException.Invalid("Send the person's email as userName or in emails.");
        var config = await _configs.GetByOrgIdAsync(orgId, ct) ?? new OrgSsoConfig { OrgId = orgId };
        CheckDomain(email, config);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var user = await _scim.GetUserByEmailAsync(orgId, email, ct);
        var revived = false;
        if (user is not null)
        {
            if (user.ScimDeletedAt is null)
                throw ScimException.Uniqueness($"A user with email {email} already exists in this organisation. Match it with a filter on userName instead of creating it.");
            user.ScimDeletedAt = null; // the IdP deleted this person before and is creating them again
            revived = true;
        }
        else
        {
            user = new OrgUser { OrgId = orgId, Email = email, PasswordHash = string.Empty, IsSsoProvisioned = true, Name = "SSO user" };
            await _orgUsers.AddAsync(user, ct);
        }

        user.ScimUserName = input.UserName;
        user.ScimExternalId = input.ExternalId;
        user.Name = input.FullName(user.Name) ?? user.Name;
        user.IsActive = input.Active ?? true;
        await _unitOfWork.SaveChangesAsync(ct);
        if (user.IsActive) await ResyncAsync(orgId, config, [user.Id], isNew: !revived, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        Audit(orgId, null, revived ? "scim.user_restored" : "scim.user_created", "created", user.Id, new { active = user.IsActive }, ct, user.Email);
        _logger.LogInformation("ScimService.CreateUserAsync exit org={OrgId} user={UserId} revived={Revived}", orgId, user.Id, revived);
        return ScimJson.User(user, ScimBaseUrl);
    }

    public async Task<JsonObject> ReplaceUserAsync(Guid orgId, Guid id, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.ReplaceUserAsync enter org={OrgId} user={UserId}", orgId, id);
        var input = UserInput.From(ScimJson.Parse(body), requireUserName: true);
        return await ChangeUserAsync(orgId, id, input, ct);
    }

    public async Task<JsonObject> PatchUserAsync(Guid orgId, Guid id, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.PatchUserAsync enter org={OrgId} user={UserId}", orgId, id);
        var input = new UserInput();
        foreach (var (op, path, value) in PatchOperations(ScimJson.Parse(body)))
        {
            if (op == "remove")
            {
                // Removing what Flowamaz requires (email, name, active) isn't possible; externalId may go.
                if (path?.Equals("externalId", StringComparison.OrdinalIgnoreCase) == true) { input.ExternalIdSet = true; input.ExternalId = null; continue; }
                throw ScimException.Mutability($"'{path}' can't be removed from a Flowamaz user.");
            }
            if (path is null)
            {
                if (value.ValueKind != JsonValueKind.Object) throw ScimException.Syntax("A patch operation without a path needs an object value.");
                input.Merge(UserInput.From(value, requireUserName: false));
            }
            else input.ApplyPath(path, value);
        }
        return await ChangeUserAsync(orgId, id, input, ct);
    }

    public async Task DeleteUserAsync(Guid orgId, Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.DeleteUserAsync enter org={OrgId} user={UserId}", orgId, id);
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var user = await _scim.GetUserAsync(orgId, id, ct: ct) ?? throw ScimException.NotFound("This user");
        if (user.IsOrgOwner)
            throw ScimException.Mutability("Organisation owners can't be deleted through SCIM. Unassign the Flowamaz app for other people only, or make someone else the owner in Flowamaz first.");

        user.ScimDeletedAt = DateTime.UtcNow;
        user.ScimExternalId = null;
        user.IsActive = false;
        foreach (var row in await _scim.GetMembershipRowsForUserAsync(orgId, user.Id, ct)) _scim.RemoveMember(row);
        await _refreshTokens.RevokeAllForUserAsync(user.Id, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        Audit(orgId, null, "scim.user_deleted", "deactivated", user.Id, null, ct, user.Email);
    }

    private async Task<JsonObject> ChangeUserAsync(Guid orgId, Guid id, UserInput input, CancellationToken ct)
    {
        var config = await _configs.GetByOrgIdAsync(orgId, ct) ?? new OrgSsoConfig { OrgId = orgId };
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var user = await _scim.GetUserAsync(orgId, id, ct: ct) ?? throw ScimException.NotFound("This user");

        if (input.Email is { } email && email != user.Email)
        {
            if (user.IsOrgOwner)
                throw ScimException.Mutability("An organisation owner's email can't be changed through SCIM. Change it in Flowamaz instead.");
            CheckDomain(email, config);
            if (await _scim.GetUserByEmailAsync(orgId, email, ct) is not null)
                throw ScimException.Uniqueness($"Another user in this organisation already has the email {email}.");
            user.Email = email;
        }
        if (input.UserNameSet) user.ScimUserName = input.UserName;
        if (input.ExternalIdSet) user.ScimExternalId = input.ExternalId;
        if (input.FullName(user.Name) is { } name) user.Name = name;

        var wasActive = user.IsActive;
        if (input.Active is { } active && active != user.IsActive)
        {
            if (!active && user.IsOrgOwner)
                throw ScimException.Mutability("Organisation owners can't be deactivated through SCIM. Unassign the Flowamaz app for other people only, or make someone else the owner in Flowamaz first.");
            user.IsActive = active;
            if (!active) await _refreshTokens.RevokeAllForUserAsync(user.Id, ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);
        if (user.IsActive && !wasActive) await ResyncAsync(orgId, config, [user.Id], isNew: false, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (wasActive != user.IsActive)
            Audit(orgId, null, user.IsActive ? "scim.user_activated" : "scim.user_deactivated", "updated", user.Id, null, ct, user.Email);
        return ScimJson.User(user, ScimBaseUrl);
    }

    private static void CheckDomain(string email, OrgSsoConfig config)
    {
        var domain = email[(email.LastIndexOf('@') + 1)..];
        if (config.AllowedEmailDomains.Count > 0 && !config.AllowedEmailDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            throw ScimException.Invalid($"Email addresses at '{domain}' aren't allowed in this organisation (SSO settings → Allowed email domains).");
    }

    // ── Groups ──────────────────────────────────────────────────────────────────────────

    public async Task<JsonObject> ListGroupsAsync(Guid orgId, string? filter, int? startIndex, int? count, bool includeMembers, CancellationToken ct = default)
    {
        var parsed = ScimJson.GroupFilter(filter);
        var (skip, take, start) = Page(startIndex, count);
        var (groups, total) = await _scim.QueryGroupsAsync(orgId, parsed?.Item1, parsed?.Item2, skip, take, ct);
        var members = includeMembers && groups.Count > 0
            ? (await _scim.GetMembersAsync(groups.Select(g => g.Id).ToList(), ct)).ToLookup(m => m.GroupId)
            : null;
        return ScimJson.List(groups.Select(g => (JsonNode)ScimJson.Group(g, members?[g.Id], ScimBaseUrl)), total, start);
    }

    public async Task<JsonObject> GetGroupAsync(Guid orgId, Guid id, bool includeMembers, CancellationToken ct = default)
    {
        var group = await _scim.GetGroupAsync(orgId, id, ct) ?? throw ScimException.NotFound("This group");
        return ScimJson.Group(group, includeMembers ? await _scim.GetMembersAsync([group.Id], ct) : null, ScimBaseUrl);
    }

    public async Task<JsonObject> CreateGroupAsync(Guid orgId, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.CreateGroupAsync enter org={OrgId}", orgId);
        var json = ScimJson.Parse(body);
        var name = DisplayName(ScimJson.String(json, "displayName"));
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        if (await _scim.GroupNameTakenAsync(orgId, name, null, ct))
            throw ScimException.Uniqueness($"A group named '{name}' already exists. Match it with a filter on displayName instead of creating it.");
        var group = new ScimGroup { OrgId = orgId, DisplayName = name, ExternalId = ExternalId(ScimJson.String(json, "externalId")) };
        await _scim.AddGroupAsync(group, ct);
        var affected = new HashSet<Guid>();
        if (ScimJson.TryGet(json, "members", out var members))
            await SetMembersAsync(orgId, group, await MemberIdsAsync(orgId, members, ct), affected, ct);
        await FinishGroupChangeAsync(orgId, affected, ct);
        await transaction.CommitAsync(ct);
        Audit(orgId, null, "scim.group_created", "created", group.Id, new { name, members = affected.Count }, ct);
        return ScimJson.Group(group, await _scim.GetMembersAsync([group.Id], ct), ScimBaseUrl);
    }

    public async Task<JsonObject> ReplaceGroupAsync(Guid orgId, Guid id, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.ReplaceGroupAsync enter org={OrgId} group={GroupId}", orgId, id);
        var json = ScimJson.Parse(body);
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var group = await _scim.GetGroupAsync(orgId, id, ct) ?? throw ScimException.NotFound("This group");
        var affected = new HashSet<Guid>();
        await RenameAsync(orgId, group, DisplayName(ScimJson.String(json, "displayName")), affected, ct);
        group.ExternalId = ExternalId(ScimJson.String(json, "externalId")) ?? group.ExternalId;
        var members = ScimJson.TryGet(json, "members", out var m) ? await MemberIdsAsync(orgId, m, ct) : [];
        await SetMembersAsync(orgId, group, members, affected, ct);
        await FinishGroupChangeAsync(orgId, affected, ct);
        await transaction.CommitAsync(ct);
        Audit(orgId, null, "scim.group_updated", "updated", group.Id, new { name = group.DisplayName, changed = affected.Count }, ct);
        return ScimJson.Group(group, await _scim.GetMembersAsync([group.Id], ct), ScimBaseUrl);
    }

    public async Task<JsonObject?> PatchGroupAsync(Guid orgId, Guid id, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.PatchGroupAsync enter org={OrgId} group={GroupId}", orgId, id);
        var operations = PatchOperations(ScimJson.Parse(body));
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var group = await _scim.GetGroupAsync(orgId, id, ct) ?? throw ScimException.NotFound("This group");
        var affected = new HashSet<Guid>();

        foreach (var (op, path, value) in operations)
        {
            var target = path?.Trim();
            if (target is null)
            {
                if (value.ValueKind != JsonValueKind.Object) throw ScimException.Syntax("A patch operation without a path needs an object value.");
                if (ScimJson.String(value, "displayName") is { } newName) await RenameAsync(orgId, group, DisplayName(newName), affected, ct);
                if (ScimJson.String(value, "externalId") is { } ext) group.ExternalId = ExternalId(ext);
                if (ScimJson.TryGet(value, "members", out var m))
                {
                    var ids = await MemberIdsAsync(orgId, m, ct);
                    if (op == "replace") await SetMembersAsync(orgId, group, ids, affected, ct);
                    else await AddMembersAsync(group, ids, affected, ct);
                }
                continue;
            }

            if (target.Equals("displayName", StringComparison.OrdinalIgnoreCase))
            {
                if (op == "remove") throw ScimException.Mutability("A group must keep its displayName.");
                await RenameAsync(orgId, group, DisplayName(value.ValueKind == JsonValueKind.String ? value.GetString() : null), affected, ct);
            }
            else if (target.Equals("externalId", StringComparison.OrdinalIgnoreCase))
            {
                group.ExternalId = op == "remove" ? null : ExternalId(value.ValueKind == JsonValueKind.String ? value.GetString() : null);
            }
            else if (target.Equals("members", StringComparison.OrdinalIgnoreCase))
            {
                switch (op)
                {
                    case "add": await AddMembersAsync(group, await MemberIdsAsync(orgId, value, ct), affected, ct); break;
                    case "replace": await SetMembersAsync(orgId, group, await MemberIdsAsync(orgId, value, ct), affected, ct); break;
                    default:
                        // Entra removes with a value list; no value means "remove everyone".
                        var remove = value.ValueKind == JsonValueKind.Undefined || value.ValueKind == JsonValueKind.Null
                            ? null : MemberIdsFromValue(value);
                        await RemoveMembersAsync(group, remove, affected, ct);
                        break;
                }
            }
            else if (ScimJson.MemberValuePath().Match(target) is { Success: true } match)
            {
                if (op != "remove") throw ScimException.Path("Only 'remove' is supported on members[value eq \"…\"].");
                if (!Guid.TryParse(match.Groups[1].Value, out var memberId)) throw ScimException.Invalid($"'{match.Groups[1].Value}' isn't a Flowamaz user id.");
                await RemoveMembersAsync(group, [memberId], affected, ct);
            }
            else throw ScimException.Path($"Groups can't be patched at '{target}'. Use displayName, externalId or members.");
        }

        await FinishGroupChangeAsync(orgId, affected, ct);
        await transaction.CommitAsync(ct);
        Audit(orgId, null, "scim.group_updated", "updated", group.Id, new { name = group.DisplayName, changed = affected.Count }, ct);
        return null; // 204: Okta and Entra don't need the group back
    }

    public async Task DeleteGroupAsync(Guid orgId, Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("ScimService.DeleteGroupAsync enter org={OrgId} group={GroupId}", orgId, id);
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var group = await _scim.GetGroupAsync(orgId, id, ct) ?? throw ScimException.NotFound("This group");
        var affected = (await _scim.GetMembershipRowsAsync(group.Id, ct)).Select(m => m.OrgUserId).ToHashSet();
        _scim.RemoveGroup(group); // memberships cascade
        await FinishGroupChangeAsync(orgId, affected, ct);
        await transaction.CommitAsync(ct);
        Audit(orgId, null, "scim.group_deleted", "deleted", group.Id, new { name = group.DisplayName, members = affected.Count }, ct);
    }

    // ── Group helpers ───────────────────────────────────────────────────────────────────

    private async Task RenameAsync(Guid orgId, ScimGroup group, string name, HashSet<Guid> affected, CancellationToken ct)
    {
        if (name == group.DisplayName) return;
        if (await _scim.GroupNameTakenAsync(orgId, name, group.Id, ct))
            throw ScimException.Uniqueness($"A group named '{name}' already exists.");
        group.DisplayName = name;
        // Mappings match on the name, so everyone in the group may get different roles now.
        foreach (var m in await _scim.GetMembershipRowsAsync(group.Id, ct)) affected.Add(m.OrgUserId);
    }

    private async Task SetMembersAsync(Guid orgId, ScimGroup group, IReadOnlyCollection<Guid> userIds, HashSet<Guid> affected, CancellationToken ct)
    {
        var wanted = userIds.ToHashSet();
        var current = await _scim.GetMembershipRowsAsync(group.Id, ct);
        foreach (var row in current.Where(r => !wanted.Contains(r.OrgUserId)))
        {
            _scim.RemoveMember(row);
            affected.Add(row.OrgUserId);
        }
        await AddMembersAsync(group, wanted.Except(current.Select(r => r.OrgUserId)).ToList(), affected, ct);
    }

    private async Task AddMembersAsync(ScimGroup group, IReadOnlyCollection<Guid> userIds, HashSet<Guid> affected, CancellationToken ct)
    {
        var existing = (await _scim.GetMembershipRowsAsync(group.Id, ct)).Select(r => r.OrgUserId).ToHashSet();
        foreach (var userId in userIds.Where(u => !existing.Contains(u)).Distinct())
        {
            await _scim.AddMemberAsync(new ScimGroupMember { GroupId = group.Id, OrgUserId = userId }, ct);
            affected.Add(userId);
        }
    }

    private async Task RemoveMembersAsync(ScimGroup group, IReadOnlyCollection<Guid>? userIds, HashSet<Guid> affected, CancellationToken ct)
    {
        var remove = userIds?.ToHashSet();
        foreach (var row in await _scim.GetMembershipRowsAsync(group.Id, ct))
            if (remove is null || remove.Contains(row.OrgUserId))
            {
                _scim.RemoveMember(row);
                affected.Add(row.OrgUserId);
            }
    }

    /// <summary>The user ids in a <c>members</c> value, all of which must be users of this organisation.</summary>
    private async Task<List<Guid>> MemberIdsAsync(Guid orgId, JsonElement members, CancellationToken ct)
    {
        var ids = MemberIdsFromValue(members);
        if (ids.Count == 0) return ids;
        var found = (await _scim.GetUsersAsync(orgId, ids, ct)).Select(u => u.Id).ToHashSet();
        var missing = ids.FirstOrDefault(id => !found.Contains(id));
        if (missing != Guid.Empty)
            throw ScimException.Invalid($"Member '{missing}' isn't a user in this Flowamaz organisation. Provision the user before adding them to a group.");
        return ids;
    }

    private static List<Guid> MemberIdsFromValue(JsonElement members)
    {
        var items = members.ValueKind switch
        {
            JsonValueKind.Array => members.EnumerateArray().ToList(),
            JsonValueKind.Object => [members],
            _ => throw ScimException.Syntax("'members' must be a list of { \"value\": \"<user id>\" }."),
        };
        if (items.Count > ScimJson.MaxMembersPerRequest)
            throw ScimException.Invalid($"Send at most {ScimJson.MaxMembersPerRequest} members in one request.");
        var ids = new List<Guid>();
        foreach (var item in items)
        {
            var value = ScimJson.String(item, "value");
            if (!Guid.TryParse(value, out var id)) throw ScimException.Invalid($"'{value}' isn't a Flowamaz user id.");
            ids.Add(id);
        }
        return ids.Distinct().ToList();
    }

    /// <summary>Saves the group change, then updates the SSO-managed workspace roles of everyone it touched.</summary>
    private async Task FinishGroupChangeAsync(Guid orgId, IReadOnlyCollection<Guid> affected, CancellationToken ct)
    {
        await _unitOfWork.SaveChangesAsync(ct);
        if (affected.Count == 0) return;
        var config = await _configs.GetByOrgIdAsync(orgId, ct) ?? new OrgSsoConfig { OrgId = orgId };
        await ResyncAsync(orgId, config, affected, isNew: false, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task ResyncAsync(Guid orgId, OrgSsoConfig config, IReadOnlyCollection<Guid> userIds, bool isNew, CancellationToken ct)
    {
        foreach (var user in await _scim.GetUsersAsync(orgId, userIds.ToList(), ct))
        {
            if (!user.IsActive || user.IsOrgOwner) continue;
            var groups = await _scim.GetGroupNamesForUserAsync(orgId, user.Id, ct);
            await _sync.SyncAsync(user, config, groups, isNew, ct);
        }
    }

    // ── Shared helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Patch operations (RFC 7644 §3.5.2). Op names are case-insensitive: Entra sends "Replace".</summary>
    private static List<(string Op, string? Path, JsonElement Value)> PatchOperations(JsonElement body)
    {
        if (!ScimJson.TryGet(body, "Operations", out var ops) || ops.ValueKind != JsonValueKind.Array)
            throw ScimException.Syntax("A PATCH request needs an 'Operations' list.");
        var result = new List<(string, string?, JsonElement)>();
        foreach (var o in ops.EnumerateArray())
        {
            var op = ScimJson.String(o, "op")?.Trim().ToLowerInvariant();
            if (op is not ("add" or "replace" or "remove"))
                throw ScimException.Syntax($"'{op}' isn't a patch operation. Use add, replace or remove.");
            var path = ScimJson.String(o, "path");
            ScimJson.TryGet(o, "value", out var value);
            if (op != "remove" && value.ValueKind == JsonValueKind.Undefined)
                throw ScimException.Syntax($"The '{op}' operation needs a value.");
            result.Add((op, string.IsNullOrWhiteSpace(path) ? null : path, value));
        }
        if (result.Count > 100) throw ScimException.Invalid("Send at most 100 operations in one PATCH request.");
        return result;
    }

    private static (int Skip, int Take, int Start) Page(int? startIndex, int? count)
    {
        var start = Math.Max(1, startIndex ?? 1);
        var take = Math.Clamp(count ?? ScimJson.MaxPageSize, 0, ScimJson.MaxPageSize);
        return (start - 1, take, start);
    }

    private static string DisplayName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 256)
            throw ScimException.Invalid("A group needs a displayName of up to 256 characters.");
        return name;
    }

    private static string? ExternalId(string? value)
    {
        var id = value?.Trim();
        if (string.IsNullOrEmpty(id)) return null;
        if (id.Length > 256) throw ScimException.Invalid("externalId can be at most 256 characters.");
        return id;
    }

    internal static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static ScimTokenDto ToDto(ScimToken t) => new(t.Id, t.Name, t.TokenPrefix, t.CreatedAt, t.LastUsedAt);

    private void Audit(Guid orgId, Guid? actor, string eventType, string action, Guid resourceId, object? metadata, CancellationToken ct, string? label = null) =>
        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            OrgId = orgId,
            ActorUserId = actor,
            ActorType = actor.HasValue ? "user" : "system",
            ActorLabel = actor.HasValue ? null : "SCIM" + (label is null ? "" : $" ({label})"),
            EventType = eventType,
            ResourceType = eventType.StartsWith("scim.group", StringComparison.Ordinal) ? "scim_group"
                : eventType.StartsWith("scim.token", StringComparison.Ordinal) ? "scim_token" : "user",
            ResourceId = resourceId,
            Action = action,
            Metadata = metadata,
        }, ct);

    /// <summary>User attributes from a SCIM body or patch; null means "not sent".</summary>
    private sealed class UserInput
    {
        /// <summary>An email sent explicitly (emails[...]). Only this changes an existing user's email.</summary>
        public string? Email { get; private set; }

        /// <summary>userName when it looks like an email: used as the email only when creating a user without emails.</summary>
        public string? UserNameEmail { get; private set; }

        public string? UserName { get; private set; }
        public bool UserNameSet { get; private set; }
        public string? ExternalId { get; set; }
        public bool ExternalIdSet { get; set; }
        public bool? Active { get; private set; }

        private string? _formatted;
        private string? _given;
        private string? _family;

        /// <summary>The name to store: a formatted/display name, else given and family names over the current ones.</summary>
        public string? FullName(string current)
        {
            if (_formatted is not null) return _formatted;
            if (_given is null && _family is null) return null;
            var (g, f) = ScimJson.SplitName(current == "SSO user" ? "" : current);
            var name = string.Join(' ', new[] { _given ?? g, _family ?? f }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return name.Length == 0 ? null : Trim(name);
        }

        public static UserInput From(JsonElement json, bool requireUserName)
        {
            var input = new UserInput();
            var userName = ScimJson.String(json, "userName")?.Trim();
            if (requireUserName && string.IsNullOrEmpty(userName)) throw ScimException.Invalid("userName is required.");
            if (userName is not null) input.SetUserName(userName);
            if (ScimJson.TryGet(json, "externalId", out var ext))
            {
                input.ExternalIdSet = true;
                input.ExternalId = ScimService.ExternalId(ext.ValueKind == JsonValueKind.String ? ext.GetString() : null);
            }
            if (ScimJson.TryGet(json, "emails", out var emails) && ScimJson.PrimaryEmail(emails) is { } email) input.SetEmail(email);
            if (ScimJson.TryGet(json, "name", out var name) && name.ValueKind == JsonValueKind.Object) input.SetNameObject(name);
            if (ScimJson.String(json, "displayName") is { } display && !string.IsNullOrWhiteSpace(display)) input._formatted = Trim(display);
            if (ScimJson.TryGet(json, "active", out var active)) input.Active = ScimJson.Bool(active);
            return input;
        }

        public void Merge(UserInput other)
        {
            if (other.UserNameSet) SetUserName(other.UserName!);
            if (other.ExternalIdSet) { ExternalIdSet = true; ExternalId = other.ExternalId; }
            if (other.Email is not null) Email = other.Email;
            if (other._formatted is not null) _formatted = other._formatted;
            if (other._given is not null) _given = other._given;
            if (other._family is not null) _family = other._family;
            if (other.Active is not null) Active = other.Active;
        }

        public void ApplyPath(string path, JsonElement value)
        {
            var p = path.Trim();
            string Text() => value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim()
                : throw ScimException.Invalid($"'{p}' must be a string.");
            switch (p.ToLowerInvariant())
            {
                case "active": Active = ScimJson.Bool(value); break;
                case "username": SetUserName(Text()); break;
                case "externalid": ExternalIdSet = true; ExternalId = ScimService.ExternalId(Text()); break;
                case "displayname" or "name.formatted": if (Text() is { Length: > 0 } n) _formatted = Trim(n); break;
                case "name": if (value.ValueKind == JsonValueKind.Object) SetNameObject(value); break;
                case "name.givenname": _given = Text(); break;
                case "name.familyname": _family = Text(); break;
                case "emails": if (ScimJson.PrimaryEmail(value) is { } e) SetEmail(e); break;
                default:
                    if (p.StartsWith("emails[", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".value", StringComparison.OrdinalIgnoreCase))
                        SetEmail(Text());
                    // Other attributes (enterprise extension, phone numbers…) aren't stored. Ignoring them keeps the
                    // IdP's sync from failing on data Flowamaz doesn't use.
                    break;
            }
        }

        private void SetUserName(string userName)
        {
            if (userName.Length is 0 or > 256) throw ScimException.Invalid("userName must be 1–256 characters.");
            UserNameSet = true;
            UserName = userName;
            UserNameEmail = ScimJson.IsEmail(userName) ? userName.ToLowerInvariant() : null;
        }

        private void SetEmail(string email)
        {
            var e = email.Trim();
            if (!ScimJson.IsEmail(e)) throw ScimException.Invalid($"'{e}' isn't a valid email address.");
            Email = e.ToLowerInvariant();
        }

        private void SetNameObject(JsonElement name)
        {
            if (ScimJson.String(name, "formatted") is { } formatted && !string.IsNullOrWhiteSpace(formatted)) _formatted = Trim(formatted);
            if (ScimJson.String(name, "givenName") is { } given) _given = given.Trim();
            if (ScimJson.String(name, "familyName") is { } family) _family = family.Trim();
        }

        private static string Trim(string name) => name.Trim() is var n && n.Length > 256 ? n[..256] : name.Trim();
    }
}
