using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Auth.Services;

/// <summary>
/// SSO orchestration for SAML 2.0 and OIDC. Resolves the org's IdP config, delegates protocol
/// work to <see cref="ISamlProcessor"/> / <see cref="IOidcClient"/>, JIT-provisions the user with
/// the Viewer role, and issues a Flowamaz JWT — identical to password login from that point on.
/// IdP certificates and client secrets are encrypted at rest with the org-scoped secret protector.
/// </summary>
public sealed class SsoService : ISsoService
{
    private const string DefaultName = "SSO user";

    private readonly IOrgSsoConfigRepository _configs;
    private readonly IOrganisationRepository _orgs;
    private readonly IOrgUserRepository _orgUsers;
    private readonly IWorkspaceRepository _workspaces;
    private readonly IWorkspaceMemberRepository _members;
    private readonly IJwtService _jwt;
    private readonly ISecretProtector _protector;
    private readonly ISamlProcessor _saml;
    private readonly IOidcClient _oidc;
    private readonly IOidcStateStore _stateStore;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SsoService> _logger;
    private readonly IAuditService? _audit;

    public SsoService(
        IOrgSsoConfigRepository configs,
        IOrganisationRepository orgs,
        IOrgUserRepository orgUsers,
        IWorkspaceRepository workspaces,
        IWorkspaceMemberRepository members,
        IJwtService jwt,
        ISecretProtector protector,
        ISamlProcessor saml,
        IOidcClient oidc,
        IOidcStateStore stateStore,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<SsoService> logger,
        IAuditService? audit = null)
    {
        _configs = configs;
        _orgs = orgs;
        _orgUsers = orgUsers;
        _workspaces = workspaces;
        _members = members;
        _jwt = jwt;
        _protector = protector;
        _saml = saml;
        _oidc = oidc;
        _stateStore = stateStore;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _logger = logger;
        _audit = audit;
    }

    private string BaseUrl => _configuration["Platform:BaseUrl"]?.TrimEnd('/') ?? "https://app.flowamaz.io";

    public async Task<SsoStatus> GetStatusAsync(string orgSlug, CancellationToken ct = default)
    {
        _logger.LogDebug("SsoService.GetStatusAsync enter orgSlug={OrgSlug}", orgSlug);
        var org = await _orgs.GetBySlugAsync(orgSlug, ct);
        if (org is null) return new SsoStatus(false, null);
        var config = await _configs.GetByOrgIdAsync(org.Id, ct);
        var enabled = config is { IsActive: true };
        return new SsoStatus(enabled, enabled ? config!.Provider : null);
    }

    public async Task<SsoConfigDto?> GetConfigAsync(Guid orgId, CancellationToken ct = default)
    {
        var config = await _configs.GetByOrgIdAsync(orgId, ct);
        return config is null ? null : ToDto(config);
    }

    public async Task<SsoConfigDto> ConfigureAsync(
        Guid orgId, string orgSlug, ConfigureSsoRequest request, Guid configuredBy, CancellationToken ct = default)
    {
        _logger.LogInformation("SsoService.ConfigureAsync enter orgId={OrgId} provider={Provider}", orgId, request.Provider);
        if (request.Provider is not ("saml" or "oidc"))
            throw new InvalidOperationException("Provider must be 'saml' or 'oidc'.");

        var config = await _configs.GetByOrgIdAsync(orgId, ct);
        var isNew = config is null;
        config ??= new OrgSsoConfig { OrgId = orgId, CreatedBy = configuredBy };

        config.Provider = request.Provider;
        config.IsActive = request.IsActive;
        config.SpEntityId = $"{BaseUrl}/saml/{orgSlug}";

        config.IdpEntityId = request.IdpEntityId;
        config.IdpSsoUrl = request.IdpSsoUrl;
        if (!string.IsNullOrWhiteSpace(request.IdpCertificate))
            config.EncryptedIdpCertificate = _protector.Protect(orgId, request.IdpCertificate);

        config.IssuerUrl = request.IssuerUrl;
        config.ClientId = request.ClientId;
        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
            config.EncryptedClientSecret = _protector.Protect(orgId, request.ClientSecret);
        if (request.Scopes is { Count: > 0 })
            config.Scopes = request.Scopes.ToList();

        if (isNew) await _configs.AddAsync(config, ct);
        else _configs.Update(config);
        await _unitOfWork.SaveChangesAsync(ct);

        return ToDto(config);
    }

    public async Task DisableAsync(Guid orgId, CancellationToken ct = default)
    {
        var config = await _configs.GetByOrgIdAsync(orgId, ct);
        if (config is null) return;
        config.IsActive = false;
        _configs.Update(config);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("SsoService.DisableAsync exit orgId={OrgId}", orgId);
    }

    public async Task<OrgUser> ProvisionUserFromSsoAsync(string email, Guid orgId, SsoClaims claims, CancellationToken ct = default)
    {
        _logger.LogDebug("SsoService.ProvisionUserFromSsoAsync enter orgId={OrgId} email={Email}", orgId, email);

        var existing = await _orgUsers.GetByEmailAndOrgAsync(email, orgId, ct);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(claims.Name) && claims.Name != existing.Name)
                existing.Name = claims.Name;
            existing.LastLoginAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("SsoService.ProvisionUserFromSsoAsync exit existing user={UserId} orgId={OrgId}", existing.Id, orgId);
            return existing;
        }

        var user = new OrgUser
        {
            OrgId = orgId,
            Email = email,
            Name = string.IsNullOrWhiteSpace(claims.Name) ? DefaultName : claims.Name,
            PasswordHash = string.Empty, // SSO accounts never password-login
            IsActive = true,
            IsSsoProvisioned = true,
            LastLoginAt = DateTime.UtcNow,
        };
        await _orgUsers.AddAsync(user, ct);

        // Default Viewer access in every existing workspace of the org.
        var workspaces = await _workspaces.GetForOrgAsync(orgId, ct);
        foreach (var workspace in workspaces)
        {
            await _members.AddAsync(
                new WorkspaceMember { WorkspaceId = workspace.Id, OrgUserId = user.Id, Role = WorkspaceRole.Viewer, IsActive = true },
                ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("SsoService.ProvisionUserFromSsoAsync exit new user={UserId} orgId={OrgId} workspaces={Count}",
            user.Id, orgId, workspaces.Count);
        return user;
    }

    // ── SAML ──────────────────────────────────────────────────────────────────

    public async Task<string> GetSpMetadataAsync(string orgSlug, CancellationToken ct = default)
    {
        var spEntityId = $"{BaseUrl}/saml/{orgSlug}";
        var acsUrl = $"{BaseUrl}/api/v1/saml/{orgSlug}/acs";
        await Task.CompletedTask;
        return _saml.BuildSpMetadata(spEntityId, acsUrl);
    }

    public async Task<string> InitiateSamlLoginAsync(string orgSlug, string? returnUrl, CancellationToken ct = default)
    {
        var (_, config) = await ResolveAsync(orgSlug, ct);
        if (config.IdpSsoUrl is null || config.SpEntityId is null)
            throw new InvalidOperationException("SAML SSO is not fully configured for this organisation.");
        return _saml.BuildAuthnRequestUrl(config.IdpSsoUrl, config.SpEntityId, returnUrl);
    }

    public async Task<SsoLoginResult> HandleSamlCallbackAsync(string orgSlug, string samlResponse, CancellationToken ct = default)
    {
        var (org, config) = await ResolveAsync(orgSlug, ct);
        if (string.IsNullOrWhiteSpace(config.EncryptedIdpCertificate))
            throw new InvalidOperationException("No IdP certificate configured for SAML validation.");

        var certificate = _protector.Unprotect(org.Id, config.EncryptedIdpCertificate);
        // Throws on an invalid signature / audience / timestamp.
        var claims = _saml.ValidateResponse(samlResponse, certificate, config.SpEntityId ?? string.Empty);

        var user = await ProvisionUserFromSsoAsync(claims.Email, org.Id, claims, ct);
        return await IssueAsync(user, orgSlug, ct);
    }

    // ── OIDC ──────────────────────────────────────────────────────────────────

    public async Task<string> InitiateOidcLoginAsync(string orgSlug, string? returnUrl, CancellationToken ct = default)
    {
        var (_, config) = await ResolveAsync(orgSlug, ct);
        if (config.IssuerUrl is null || config.ClientId is null)
            throw new InvalidOperationException("OIDC SSO is not fully configured for this organisation.");

        var state = await _stateStore.CreateAsync(orgSlug, ct);
        var redirectUri = $"{BaseUrl}/api/v1/oidc/{orgSlug}/callback";
        return _oidc.BuildAuthorizeUrl(config.IssuerUrl, config.ClientId, redirectUri, config.Scopes, state);
    }

    public async Task<SsoLoginResult> HandleOidcCallbackAsync(string orgSlug, string code, string state, CancellationToken ct = default)
    {
        var stateOrg = await _stateStore.ConsumeAsync(state, ct);
        if (stateOrg is null || !string.Equals(stateOrg, orgSlug, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid or expired OIDC state. Restart the sign-in flow.");

        var (org, config) = await ResolveAsync(orgSlug, ct);
        if (config.IssuerUrl is null || config.ClientId is null || config.EncryptedClientSecret is null)
            throw new InvalidOperationException("OIDC SSO is not fully configured for this organisation.");

        var secret = _protector.Unprotect(org.Id, config.EncryptedClientSecret);
        var redirectUri = $"{BaseUrl}/api/v1/oidc/{orgSlug}/callback";
        var claims = await _oidc.ExchangeCodeAsync(config.IssuerUrl, config.ClientId, secret, redirectUri, code, ct);

        var user = await ProvisionUserFromSsoAsync(claims.Email, org.Id, claims, ct);
        return await IssueAsync(user, orgSlug, ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<(Organisation Org, OrgSsoConfig Config)> ResolveAsync(string orgSlug, CancellationToken ct)
    {
        var org = await _orgs.GetBySlugAsync(orgSlug, ct)
            ?? throw new InvalidOperationException($"Organisation '{orgSlug}' was not found.");
        var config = await _configs.GetByOrgIdAsync(org.Id, ct);
        if (config is not { IsActive: true })
            throw new InvalidOperationException($"SSO is not enabled for organisation '{orgSlug}'.");
        return (org, config);
    }

    private async Task<SsoLoginResult> IssueAsync(OrgUser user, string orgSlug, CancellationToken ct)
    {
        var memberships = await _members.GetActiveMembershipsForUserAsync(user.Id, ct);
        var token = _jwt.GenerateAccessToken(user, orgSlug, memberships);

        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            OrgId = user.OrgId,
            ActorUserId = user.Id,
            ActorType = "user",
            ActorLabel = user.Email,
            EventType = "sso.login_succeeded",
            ResourceType = "sso",
            ResourceId = user.Id,
            ResourceLabel = user.Email,
            Action = "created",
        }, ct);

        return new SsoLoginResult(user, token, orgSlug);
    }

    private static SsoConfigDto ToDto(OrgSsoConfig c) =>
        new(c.OrgId, c.Provider, c.IsActive, c.IdpEntityId, c.IdpSsoUrl, c.SpEntityId, c.IssuerUrl, c.ClientId,
            c.Scopes, c.EncryptedIdpCertificate is not null, c.EncryptedClientSecret is not null);
}
