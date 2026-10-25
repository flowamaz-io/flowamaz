using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Auth.Services;

/// <summary>
/// Single sign-on for Enterprise organisations (FUNCTIONAL.md §12.6).
/// <list type="bullet">
/// <item>Only organisations whose plan includes SSO can configure or use it.</item>
/// <item>SAML 2.0 (SP-initiated with InResponseTo checks, or IdP-initiated) and OIDC (code + PKCE + nonce).
/// Protocol checks live in <see cref="ISamlProcessor"/> / <see cref="IOidcClient"/>; this class ties each response to
/// the sign-in that started it, blocks replays, and decides who gets in.</item>
/// <item>Just-in-time accounts are optional. With group mappings configured, the IdP decides workspace roles at
/// every sign-in; org owners are never changed by SSO.</item>
/// <item>"Require SSO" blocks password sign-in for everyone except org owners (break-glass).</item>
/// </list>
/// Sessions are issued by the caller (the same refresh-cookie session as password sign-in), never put in a URL.
/// </summary>
public sealed class SsoService : ISsoService
{
    private const string DefaultName = "SSO user";

    private readonly IOrgSsoConfigRepository _configs;
    private readonly IOrganisationRepository _orgs;
    private readonly IPlanRepository _plans;
    private readonly IOrgUserRepository _orgUsers;
    private readonly IWorkspaceRepository _workspaces;
    private readonly IWorkspaceMemberRepository _members;
    private readonly ISecretProtector _protector;
    private readonly ISamlProcessor _saml;
    private readonly IOidcClient _oidc;
    private readonly ISsoStateStore _states;
    private readonly ISsoReplayCache _replay;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SsoService> _logger;
    private readonly IAuditService? _audit;

    public SsoService(
        IOrgSsoConfigRepository configs,
        IOrganisationRepository orgs,
        IPlanRepository plans,
        IOrgUserRepository orgUsers,
        IWorkspaceRepository workspaces,
        IWorkspaceMemberRepository members,
        ISecretProtector protector,
        ISamlProcessor saml,
        IOidcClient oidc,
        ISsoStateStore states,
        ISsoReplayCache replay,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<SsoService> logger,
        IAuditService? audit = null)
    {
        _configs = configs;
        _orgs = orgs;
        _plans = plans;
        _orgUsers = orgUsers;
        _workspaces = workspaces;
        _members = members;
        _protector = protector;
        _saml = saml;
        _oidc = oidc;
        _states = states;
        _replay = replay;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _logger = logger;
        _audit = audit;
    }

    private string BaseUrl
    {
        get
        {
            var configured = _configuration["Platform:BaseUrl"];
            return string.IsNullOrWhiteSpace(configured) ? "https://app.flowamaz.io" : configured.TrimEnd('/');
        }
    }

    private string SpEntityId(string orgSlug) => $"{BaseUrl}/saml/{orgSlug}";
    private string AcsUrl(string orgSlug) => $"{BaseUrl}/api/v1/saml/{orgSlug}/acs";
    private string MetadataUrl(string orgSlug) => $"{BaseUrl}/api/v1/saml/{orgSlug}/metadata";
    private string OidcRedirectUri(string orgSlug) => $"{BaseUrl}/api/v1/oidc/{orgSlug}/callback";

    // ── Status & configuration ──────────────────────────────────────────────────────────

    public async Task<SsoStatus> GetStatusAsync(string orgSlug, CancellationToken ct = default)
    {
        var org = await _orgs.GetBySlugAsync(orgSlug, ct);
        if (org is null) return new SsoStatus(false, null);
        var config = await _configs.GetByOrgIdAsync(org.Id, ct);
        var enabled = config is { IsActive: true } && await PlanIncludesSsoAsync(org, ct);
        return new SsoStatus(enabled, enabled ? config!.Provider : null, enabled && config!.RequireSso);
    }

    public async Task<SsoConfigDto?> GetConfigAsync(Guid orgId, CancellationToken ct = default)
    {
        var org = await _orgs.GetByIdAsync(orgId, ct);
        if (org is null) return null;
        var config = await _configs.GetByOrgIdAsync(orgId, ct);
        var onPlan = await PlanIncludesSsoAsync(org, ct);
        // An org without a config still gets the URLs its IdP admin needs, and whether its plan allows SSO.
        return ToDto(config ?? new OrgSsoConfig { OrgId = orgId, Provider = "saml" }, org.Slug, onPlan);
    }

    public async Task<SsoConfigDto> ConfigureAsync(
        Guid orgId, string orgSlug, ConfigureSsoRequest request, Guid configuredBy, CancellationToken ct = default)
    {
        _logger.LogInformation("SsoService.ConfigureAsync enter org={OrgId} provider={Provider} active={Active}", orgId, request.Provider, request.IsActive);
        var org = await _orgs.GetByIdAsync(orgId, ct) ?? throw SsoLoginException.NotEnabled(orgSlug);
        if (!await PlanIncludesSsoAsync(org, ct)) throw SsoLoginException.NotOnPlan();
        if (request.Provider is not ("saml" or "oidc"))
            throw Invalid("Choose SAML or OIDC as the provider.");

        var config = await _configs.GetByOrgIdAsync(orgId, ct);
        var isNew = config is null;
        config ??= new OrgSsoConfig { OrgId = orgId, CreatedBy = configuredBy };

        config.Provider = request.Provider;
        config.SpEntityId = SpEntityId(org.Slug);
        config.IdpEntityId = Clean(request.IdpEntityId);
        config.IdpSsoUrl = Clean(request.IdpSsoUrl);
        if (!string.IsNullOrWhiteSpace(request.IdpCertificate))
        {
            ValidateCertificate(request.IdpCertificate);
            config.EncryptedIdpCertificate = _protector.Protect(orgId, request.IdpCertificate.Trim());
        }
        config.IssuerUrl = Clean(request.IssuerUrl);
        config.ClientId = Clean(request.ClientId);
        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
            config.EncryptedClientSecret = _protector.Protect(orgId, request.ClientSecret.Trim());
        if (request.Scopes is { Count: > 0 })
            config.Scopes = request.Scopes.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

        if (request.RequireSso is { } require) config.RequireSso = require;
        if (request.JitProvisioning is { } jit) config.JitProvisioning = jit;
        if (request.DefaultRole is { } role) config.DefaultRole = role;
        if (request.GroupsAttribute is { } attribute)
            config.GroupsAttribute = string.IsNullOrWhiteSpace(attribute) ? "groups" : attribute.Trim();
        if (request.GroupRoleMappings is { } mappings)
            config.GroupRoleMappings = await ValidateMappingsAsync(orgId, mappings, ct);

        config.IsActive = request.IsActive;
        if (config.IsActive && MissingForActivation(config) is { } missing)
            throw Invalid($"SSO can't be turned on yet: {missing}");
        if (config.RequireSso && !config.IsActive)
            config.RequireSso = false; // requiring a sign-in method that is off would lock members out

        if (isNew) await _configs.AddAsync(config, ct);
        else _configs.Update(config);
        await _unitOfWork.SaveChangesAsync(ct);

        Audit(orgId, configuredBy, "sso.configured", "updated", new
        {
            provider = config.Provider, active = config.IsActive, requireSso = config.RequireSso,
            jit = config.JitProvisioning, mappings = config.GroupRoleMappings.Count,
        }, ct);
        _logger.LogInformation("SsoService.ConfigureAsync exit org={OrgId}", orgId);
        return ToDto(config, org.Slug, planIncludesSso: true);
    }

    public async Task DisableAsync(Guid orgId, CancellationToken ct = default)
    {
        var config = await _configs.GetByOrgIdAsync(orgId, ct);
        if (config is null) return;
        config.IsActive = false;
        config.RequireSso = false;
        _configs.Update(config);
        await _unitOfWork.SaveChangesAsync(ct);
        Audit(orgId, null, "sso.disabled", "updated", null, ct);
    }

    public async Task<bool> IsPasswordLoginBlockedAsync(Guid orgId, OrgUser user, CancellationToken ct = default)
    {
        if (user.IsOrgOwner) return false; // break-glass: owners keep password sign-in
        var config = await _configs.GetByOrgIdAsync(orgId, ct);
        if (config is not { IsActive: true, RequireSso: true }) return false;
        var org = await _orgs.GetByIdAsync(orgId, ct);
        return org is not null && await PlanIncludesSsoAsync(org, ct);
    }

    // ── Who gets in ─────────────────────────────────────────────────────────────────────

    public async Task<OrgUser> ProvisionUserFromSsoAsync(string email, Guid orgId, SsoClaims claims, CancellationToken ct = default)
    {
        var config = await _configs.GetByOrgIdAsync(orgId, ct) ?? new OrgSsoConfig { OrgId = orgId };
        var normalised = email.Trim().ToLowerInvariant();
        var user = await _orgUsers.GetByEmailAndOrgAsync(normalised, orgId, ct);

        if (user is not null)
        {
            if (!user.IsActive) throw SsoLoginException.Deactivated();
            if (!string.IsNullOrWhiteSpace(claims.Name) && claims.Name != user.Name) user.Name = claims.Name;
            user.LastLoginAt = DateTime.UtcNow;
            if (!user.IsOrgOwner && config.GroupRoleMappings.Count > 0)
                await ApplyGroupRolesAsync(user, config, claims.GroupList, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return user;
        }

        if (!config.JitProvisioning) throw SsoLoginException.NoAccount();

        user = new OrgUser
        {
            OrgId = orgId,
            Email = normalised,
            Name = string.IsNullOrWhiteSpace(claims.Name) ? DefaultName : claims.Name,
            PasswordHash = string.Empty, // SSO accounts never password-login
            IsActive = true,
            IsSsoProvisioned = true,
            LastLoginAt = DateTime.UtcNow,
        };
        await _orgUsers.AddAsync(user, ct);
        await ApplyGroupRolesAsync(user, config, claims.GroupList, ct, isNew: true);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("SsoService.ProvisionUserFromSsoAsync created user={UserId} org={OrgId}", user.Id, orgId);
        return user;
    }

    /// <summary>
    /// Sets the user's role in each workspace from their IdP groups (highest matching role; <c>DefaultRole</c> when
    /// none match). New users get memberships everywhere; existing users are updated only when mappings exist.
    /// Never demotes a workspace's last active Admin.
    /// </summary>
    private async Task ApplyGroupRolesAsync(OrgUser user, OrgSsoConfig config, IReadOnlyList<string> groups, CancellationToken ct, bool isNew = false)
    {
        var groupSet = groups.ToHashSet(StringComparer.Ordinal);
        foreach (var workspace in await _workspaces.GetForOrgAsync(user.OrgId, ct))
        {
            var matched = config.GroupRoleMappings
                .Where(m => groupSet.Contains(m.Group) && (m.WorkspaceId is null || m.WorkspaceId == workspace.Id))
                .Select(m => (WorkspaceRole?)m.Role)
                .Max();
            var role = matched ?? config.DefaultRole;

            var member = isNew ? null : await _members.GetAsync(workspace.Id, user.Id, ct);
            if (member is null)
            {
                await _members.AddAsync(new WorkspaceMember { WorkspaceId = workspace.Id, OrgUserId = user.Id, Role = role, IsActive = true }, ct);
                continue;
            }
            if (member.Role == role && member.IsActive) continue;
            if (member.Role == WorkspaceRole.Admin && role != WorkspaceRole.Admin
                && await _members.CountActiveAdminsAsync(workspace.Id, ct) <= 1)
            {
                _logger.LogWarning("SsoService kept last Admin of workspace={WorkspaceId} user={UserId} (IdP groups would demote)", workspace.Id, user.Id);
                continue;
            }
            member.Role = role;
            member.IsActive = true;
            _members.Update(member);
        }
    }

    // ── SAML ────────────────────────────────────────────────────────────────────────────

    public async Task<string> GetSpMetadataAsync(string orgSlug, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return _saml.BuildSpMetadata(SpEntityId(orgSlug), AcsUrl(orgSlug));
    }

    public async Task<string> InitiateSamlLoginAsync(string orgSlug, string? returnUrl, CancellationToken ct = default)
    {
        var (_, config) = await ResolveAsync(orgSlug, "saml", ct);
        var requestId = "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
        var key = await _states.CreateAsync(new SsoLoginState(orgSlug, "saml", SafeReturnPath(returnUrl), SamlRequestId: requestId), ct);
        return _saml.BuildAuthnRequest(config.IdpSsoUrl!, SpEntityId(orgSlug), AcsUrl(orgSlug), requestId, key).RedirectUrl;
    }

    public async Task<SsoLoginResult> HandleSamlCallbackAsync(string orgSlug, string samlResponse, string? relayState, CancellationToken ct = default)
    {
        var (org, config) = await ResolveAsync(orgSlug, "saml", ct);
        try
        {
            var certificate = _protector.Unprotect(org.Id, config.EncryptedIdpCertificate!);
            var assertion = _saml.ValidateResponse(samlResponse, new SamlExpectations(
                certificate, config.IdpEntityId!, SpEntityId(orgSlug), AcsUrl(orgSlug), config.GroupsAttribute));

            var returnPath = "/";
            if (assertion.InResponseTo is not null)
            {
                // SP-initiated: the response must answer the request this browser started.
                var state = relayState is null ? null : await _states.ConsumeAsync(relayState, ct);
                if (state is null || state.Protocol != "saml" || !string.Equals(state.OrgSlug, orgSlug, StringComparison.OrdinalIgnoreCase)
                    || state.SamlRequestId != assertion.InResponseTo)
                    throw SsoLoginException.Expired();
                returnPath = state.ReturnPath;
            }

            if (!await _replay.TryRecordAsync(orgSlug, assertion.AssertionId, assertion.NotOnOrAfterUtc, ct))
                throw SsoLoginException.Expired();

            var user = await ProvisionUserFromSsoAsync(assertion.Claims.Email, org.Id, assertion.Claims, ct);
            Audit(org.Id, user.Id, "sso.login_succeeded", "signed_in", new { provider = "saml" }, ct, user.Email);
            return new SsoLoginResult(user, org.Slug, returnPath);
        }
        catch (SsoLoginException ex)
        {
            Audit(org.Id, null, "sso.login_failed", "refused", new { provider = "saml", reason = ex.ErrorCode }, ct);
            throw;
        }
    }

    // ── OIDC ────────────────────────────────────────────────────────────────────────────

    public async Task<string> InitiateOidcLoginAsync(string orgSlug, string? returnUrl, CancellationToken ct = default)
    {
        var (_, config) = await ResolveAsync(orgSlug, "oidc", ct);
        var discovery = await _oidc.DiscoverAsync(config.IssuerUrl!, ct);
        var nonce = RandomUrlToken();
        var verifier = RandomUrlToken();
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var key = await _states.CreateAsync(
            new SsoLoginState(orgSlug, "oidc", SafeReturnPath(returnUrl), OidcNonce: nonce, OidcCodeVerifier: verifier), ct);
        return _oidc.BuildAuthorizeUrl(discovery, config.ClientId!, OidcRedirectUri(orgSlug), config.Scopes, key, nonce, challenge);
    }

    public async Task<SsoLoginResult> HandleOidcCallbackAsync(string orgSlug, string code, string state, CancellationToken ct = default)
    {
        var (org, config) = await ResolveAsync(orgSlug, "oidc", ct);
        try
        {
            var saved = await _states.ConsumeAsync(state, ct);
            if (saved is null || saved.Protocol != "oidc" || !string.Equals(saved.OrgSlug, orgSlug, StringComparison.OrdinalIgnoreCase)
                || saved.OidcNonce is null || saved.OidcCodeVerifier is null)
                throw SsoLoginException.Expired();

            var secret = _protector.Unprotect(org.Id, config.EncryptedClientSecret!);
            var discovery = await _oidc.DiscoverAsync(config.IssuerUrl!, ct);
            var claims = await _oidc.ExchangeCodeAsync(discovery, config.ClientId!, secret, OidcRedirectUri(orgSlug),
                code, saved.OidcCodeVerifier, saved.OidcNonce, config.GroupsAttribute, ct);

            var user = await ProvisionUserFromSsoAsync(claims.Email, org.Id, claims, ct);
            Audit(org.Id, user.Id, "sso.login_succeeded", "signed_in", new { provider = "oidc" }, ct, user.Email);
            return new SsoLoginResult(user, org.Slug, saved.ReturnPath);
        }
        catch (SsoLoginException ex)
        {
            Audit(org.Id, null, "sso.login_failed", "refused", new { provider = "oidc", reason = ex.ErrorCode }, ct);
            throw;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    private async Task<(Organisation Org, OrgSsoConfig Config)> ResolveAsync(string orgSlug, string protocol, CancellationToken ct)
    {
        var org = await _orgs.GetBySlugAsync(orgSlug, ct) ?? throw SsoLoginException.NotEnabled(orgSlug);
        var config = await _configs.GetByOrgIdAsync(org.Id, ct);
        if (config is not { IsActive: true } || config.Provider != protocol || MissingForActivation(config) is not null)
            throw SsoLoginException.NotEnabled(orgSlug);
        if (!await PlanIncludesSsoAsync(org, ct)) throw SsoLoginException.NotOnPlan();
        return (org, config);
    }

    private async Task<bool> PlanIncludesSsoAsync(Organisation org, CancellationToken ct) =>
        (await _plans.GetByIdAsync(org.PlanId, ct))?.Features.Sso ?? false;

    private static string? MissingForActivation(OrgSsoConfig c) => c.Provider switch
    {
        "saml" when string.IsNullOrWhiteSpace(c.IdpEntityId) => "enter the IdP entity ID (issuer).",
        "saml" when string.IsNullOrWhiteSpace(c.IdpSsoUrl) => "enter the IdP sign-in URL.",
        "saml" when c.EncryptedIdpCertificate is null => "paste the IdP signing certificate.",
        "oidc" when string.IsNullOrWhiteSpace(c.IssuerUrl) => "enter the issuer URL.",
        "oidc" when string.IsNullOrWhiteSpace(c.ClientId) => "enter the client ID.",
        "oidc" when c.EncryptedClientSecret is null => "enter the client secret.",
        _ => null,
    };

    private async Task<List<SsoGroupRoleMapping>> ValidateMappingsAsync(Guid orgId, IReadOnlyList<SsoGroupRoleMapping> mappings, CancellationToken ct)
    {
        if (mappings.Count > 200) throw Invalid("Use at most 200 group mappings.");
        var workspaceIds = (await _workspaces.GetForOrgAsync(orgId, ct)).Select(w => w.Id).ToHashSet();
        var result = new List<SsoGroupRoleMapping>();
        foreach (var m in mappings)
        {
            var group = m.Group?.Trim() ?? "";
            if (group.Length is 0 or > 256) throw Invalid("Every group mapping needs a group name (up to 256 characters).");
            if (!Enum.IsDefined(m.Role)) throw Invalid($"'{m.Role}' isn't a workspace role.");
            if (m.WorkspaceId is { } ws && !workspaceIds.Contains(ws))
                throw Invalid("A group mapping points to a workspace that isn't in this organisation.");
            result.Add(m with { Group = group });
        }
        return result;
    }

    private static void ValidateCertificate(string pem)
    {
        try
        {
            var text = pem.Contains("BEGIN CERTIFICATE") ? pem : $"-----BEGIN CERTIFICATE-----\n{pem.Trim()}\n-----END CERTIFICATE-----";
            using var cert = X509Certificate2.CreateFromPem(text);
            using var key = cert.GetRSAPublicKey();
            if (key is null) throw Invalid("The IdP certificate must have an RSA key.");
        }
        catch (CryptographicException)
        {
            throw Invalid("The IdP certificate isn't a valid X.509 certificate. Paste the PEM (-----BEGIN CERTIFICATE-----) from your IdP.");
        }
    }

    /// <summary>Only same-site paths, so SSO can't be used to bounce people to another site after sign-in.</summary>
    internal static string SafeReturnPath(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return "/";
        var path = returnUrl.Trim();
        if (!path.StartsWith('/') || path.StartsWith("//") || path.StartsWith("/\\") || path.Contains('\\')
            || path.Any(char.IsControl) || path.Length > 512) return "/";
        return path;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SsoLoginException Invalid(string message) => new("SSO_CONFIG_INVALID", message, 400);

    private static string RandomUrlToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void Audit(Guid orgId, Guid? actor, string eventType, string action, object? metadata, CancellationToken ct, string? label = null) =>
        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            OrgId = orgId,
            ActorUserId = actor,
            ActorType = actor.HasValue ? "user" : "system",
            ActorLabel = label,
            EventType = eventType,
            ResourceType = "sso",
            ResourceId = orgId,
            Action = action,
            Metadata = metadata,
        }, ct);

    private SsoConfigDto ToDto(OrgSsoConfig c, string orgSlug, bool planIncludesSso) =>
        new(c.OrgId, c.Provider, c.IsActive, c.IdpEntityId, c.IdpSsoUrl, SpEntityId(orgSlug), c.IssuerUrl, c.ClientId,
            c.Scopes, c.EncryptedIdpCertificate is not null, c.EncryptedClientSecret is not null,
            c.RequireSso, c.JitProvisioning, c.DefaultRole, c.GroupsAttribute, c.GroupRoleMappings,
            AcsUrl(orgSlug), MetadataUrl(orgSlug), OidcRedirectUri(orgSlug), planIncludesSso);
}
