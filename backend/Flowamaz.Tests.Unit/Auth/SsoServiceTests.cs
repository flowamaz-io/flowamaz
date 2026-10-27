using Flowamaz.Application.Auth.Services;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Auth;

/// <summary>
/// SSO decisions (protocol checks are tested in Sso/SamlProcessorTests and Sso/OidcClientTests): Enterprise-only,
/// just-in-time accounts on/off, group → role mapping limited to SSO-managed memberships, the response tied to its
/// request and to the browser that started it (login CSRF), never replayed, IdP-initiated sign-in off by default,
/// accounts linked to their IdP subject, allowed email domains, return paths kept on-site, and "require SSO" sparing
/// org owners while ending other members' sessions.
/// </summary>
public class SsoServiceTests
{
    private readonly Mock<IOrgSsoConfigRepository> _configs = new();
    private readonly Mock<IOrganisationRepository> _orgs = new();
    private readonly Mock<IPlanRepository> _plans = new();
    private readonly Mock<IOrgUserRepository> _orgUsers = new();
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IWorkspaceMemberRepository> _members = new();
    private readonly Mock<ISecretProtector> _protector = new();
    private readonly Mock<ISamlProcessor> _saml = new();
    private readonly Mock<IOidcClient> _oidc = new();
    private readonly Mock<ISsoStateStore> _states = new();
    private readonly Mock<ISsoReplayCache> _replay = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IScimRepository> _scim = new();
    private static readonly string Binding = SsoBrowserBinding.For("relay");

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlanId = Guid.NewGuid();
    private const string OrgSlug = "acme";
    private readonly Organisation _org = new() { Id = OrgId, Slug = OrgSlug, PlanId = PlanId };
    private OrgSsoConfig _config = new();
    private bool _enterprise = true;

    public SsoServiceTests()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _orgs.Setup(o => o.GetBySlugAsync(OrgSlug, It.IsAny<CancellationToken>())).ReturnsAsync(_org);
        _orgs.Setup(o => o.GetByIdAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(_org);
        _plans.Setup(p => p.GetByIdAsync(PlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Plan { Id = PlanId, Name = _enterprise ? "Enterprise" : "Pro", Features = new PlanFeatures { Sso = _enterprise } });
        _configs.Setup(c => c.GetByOrgIdAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _config);
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _protector.Setup(p => p.Unprotect(It.IsAny<Guid>(), It.IsAny<string>())).Returns("decrypted");
        _protector.Setup(p => p.Protect(It.IsAny<Guid>(), It.IsAny<string>())).Returns("encrypted");
        _replay.Setup(r => r.TryRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _states.Setup(s => s.CreateAsync(It.IsAny<SsoLoginState>(), It.IsAny<CancellationToken>())).ReturnsAsync("state-key");
    }

    private SsoService Service() => new(
        _configs.Object, _orgs.Object, _plans.Object, _orgUsers.Object, _workspaces.Object, _members.Object, _refreshTokens.Object,
        _protector.Object, _saml.Object, _oidc.Object, _states.Object, _replay.Object, _uow.Object,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:BaseUrl"] = "https://app.flowamaz.io" }).Build(),
        NullLogger<SsoService>.Instance, scim: _scim.Object);

    private void Saml(Action<OrgSsoConfig>? change = null)
    {
        _config = new OrgSsoConfig
        {
            OrgId = OrgId, Provider = "saml", IsActive = true, IdpEntityId = "https://idp", IdpSsoUrl = "https://idp/sso",
            EncryptedIdpCertificate = "enc",
        };
        change?.Invoke(_config);
    }

    private void Oidc() => _config = new OrgSsoConfig
    {
        OrgId = OrgId, Provider = "oidc", IsActive = true, IssuerUrl = "https://idp", ClientId = "c", EncryptedClientSecret = "enc",
    };

    private void SamlReturns(string email = "jane@acme.test", string? inResponseTo = null, string assertionId = "_a1", params string[] groups) =>
        _saml.Setup(s => s.ValidateResponse(It.IsAny<string>(), It.IsAny<SamlExpectations>()))
            .Returns(new SamlAssertion(new SsoClaims(email, "Jane", groups), assertionId, DateTime.UtcNow.AddMinutes(5), inResponseTo));

    private void NoUser(string email = "jane@acme.test") =>
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync(email, OrgId, It.IsAny<CancellationToken>())).ReturnsAsync((OrgUser?)null);

    // ── Enterprise only ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_reports_sso_off_when_the_plan_lacks_it_even_if_configured()
    {
        Saml(c => c.RequireSso = true);
        _enterprise = false;
        (await Service().GetStatusAsync(OrgSlug)).Should().Be(new SsoStatus(false, null, false));
    }

    [Fact]
    public async Task Configuring_on_a_plan_without_sso_is_refused()
    {
        _enterprise = false;
        var act = () => Service().ConfigureAsync(OrgId, OrgSlug, new ConfigureSsoRequest("saml", false, null, null, null, null, null, null, null), Guid.NewGuid());
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_NOT_ON_PLAN");
    }

    [Fact]
    public async Task Signing_in_on_a_plan_without_sso_is_refused()
    {
        Saml();
        _enterprise = false;
        var act = () => Service().InitiateSamlLoginAsync(OrgSlug, null);
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_NOT_ON_PLAN");
    }

    // ── Configuration ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Turning_sso_on_without_the_idp_details_is_refused()
    {
        _config = null!;
        _configs.Setup(c => c.GetByOrgIdAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync((OrgSsoConfig?)null);
        var act = () => Service().ConfigureAsync(OrgId, OrgSlug,
            new ConfigureSsoRequest("saml", true, "https://idp", "https://idp/sso", null, null, null, null, null), Guid.NewGuid());
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.Message.Should().Contain("certificate");
    }

    [Fact]
    public async Task A_certificate_that_isnt_one_is_refused()
    {
        _configs.Setup(c => c.GetByOrgIdAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync((OrgSsoConfig?)null);
        var act = () => Service().ConfigureAsync(OrgId, OrgSlug,
            new ConfigureSsoRequest("saml", false, null, null, "not a certificate", null, null, null, null), Guid.NewGuid());
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.Message.Should().Contain("X.509");
    }

    [Fact]
    public async Task A_group_mapping_to_another_organisations_workspace_is_refused()
    {
        Saml();
        var act = () => Service().ConfigureAsync(OrgId, OrgSlug, new ConfigureSsoRequest("saml", true, null, null, null, null, null, null, null,
            GroupRoleMappings: [new SsoGroupRoleMapping("Credit", Guid.NewGuid(), WorkspaceRole.Admin)]), Guid.NewGuid());
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.Message.Should().Contain("isn't in this organisation");
    }

    [Fact]
    public async Task Require_sso_cannot_stay_on_while_sso_is_off()
    {
        Saml(c => c.RequireSso = true);
        var dto = await Service().ConfigureAsync(OrgId, OrgSlug, new ConfigureSsoRequest("saml", false, null, null, null, null, null, null, null), Guid.NewGuid());
        dto.RequireSso.Should().BeFalse();
    }

    // ── Who gets in ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_user_gets_the_default_role_only_in_the_default_workspaces()
    {
        var lending = Guid.NewGuid();
        Saml(c => { c.DefaultRole = WorkspaceRole.Runner; c.DefaultWorkspaceIds = [lending]; });
        NoUser();
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Workspace { Id = lending, OrgId = OrgId }, new Workspace { Id = Guid.NewGuid(), OrgId = OrgId }]);
        var added = new List<WorkspaceMember>();
        _members.Setup(m => m.AddAsync(It.IsAny<WorkspaceMember>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceMember, CancellationToken>((m, _) => added.Add(m)).Returns(Task.CompletedTask);

        var user = await Service().ProvisionUserFromSsoAsync("Jane@Acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane"));

        user.Email.Should().Be("jane@acme.test");
        user.IsSsoProvisioned.Should().BeTrue();
        added.Should().ContainSingle().Which.Should().Match<WorkspaceMember>(m =>
            m.WorkspaceId == lending && m.Role == WorkspaceRole.Runner && m.ManagedBySso);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Without_mappings_or_default_workspaces_a_new_user_joins_no_workspace()
    {
        Saml();
        NoUser();
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync([new Workspace { Id = Guid.NewGuid(), OrgId = OrgId }]);
        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane"));
        _members.Verify(m => m.AddAsync(It.IsAny<WorkspaceMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Without_just_in_time_accounts_an_unknown_person_is_refused()
    {
        Saml(c => c.JitProvisioning = false);
        NoUser();
        var act = () => Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane"));
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_NO_ACCOUNT");
        _orgUsers.Verify(r => r.AddAsync(It.IsAny<OrgUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_deactivated_account_is_refused()
    {
        Saml();
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.test", OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgUser { Id = Guid.NewGuid(), OrgId = OrgId, Email = "jane@acme.test", IsActive = false });
        var act = () => Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane"));
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_ACCOUNT_DEACTIVATED");
    }

    [Fact]
    public async Task Group_mappings_set_the_highest_matching_role_per_workspace()
    {
        var lending = Guid.NewGuid();
        var other = Guid.NewGuid();
        Saml(c => c.GroupRoleMappings =
        [
            new SsoGroupRoleMapping("Credit", lending, WorkspaceRole.Operator),
            new SsoGroupRoleMapping("Credit-Leads", lending, WorkspaceRole.Admin),
            new SsoGroupRoleMapping("Everyone", null, WorkspaceRole.Viewer),
        ]);
        var user = new OrgUser { Id = Guid.NewGuid(), OrgId = OrgId, Email = "jane@acme.test", IsActive = true };
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.test", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Workspace { Id = lending, OrgId = OrgId }, new Workspace { Id = other, OrgId = OrgId }]);
        var lendingMember = new WorkspaceMember { WorkspaceId = lending, OrgUserId = user.Id, Role = WorkspaceRole.Viewer, IsActive = true, ManagedBySso = true };
        _members.Setup(m => m.GetAsync(lending, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lendingMember);
        _members.Setup(m => m.GetAsync(other, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceMember?)null);
        WorkspaceMember? addedOther = null;
        _members.Setup(m => m.AddAsync(It.IsAny<WorkspaceMember>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceMember, CancellationToken>((m, _) => addedOther = m).Returns(Task.CompletedTask);

        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", ["Credit", "Credit-Leads", "Everyone"]));

        lendingMember.Role.Should().Be(WorkspaceRole.Admin);
        addedOther!.WorkspaceId.Should().Be(other);
        addedOther.Role.Should().Be(WorkspaceRole.Viewer);
        addedOther.ManagedBySso.Should().BeTrue();
    }

    private (OrgUser User, Guid Ws) MemberSetup(WorkspaceMember member)
    {
        var user = new OrgUser { Id = member.OrgUserId, OrgId = OrgId, Email = "jane@acme.test", IsActive = true };
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.test", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync([new Workspace { Id = member.WorkspaceId, OrgId = OrgId }]);
        _members.Setup(m => m.GetAsync(member.WorkspaceId, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        return (user, member.WorkspaceId);
    }

    [Fact]
    public async Task Memberships_added_by_hand_are_not_changed_by_group_mappings()
    {
        var ws = Guid.NewGuid();
        Saml(c => c.GroupRoleMappings = [new SsoGroupRoleMapping("Credit", ws, WorkspaceRole.Admin)]);
        var member = new WorkspaceMember { WorkspaceId = ws, OrgUserId = Guid.NewGuid(), Role = WorkspaceRole.Viewer, IsActive = true };
        MemberSetup(member);

        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", ["Credit"]));

        member.Role.Should().Be(WorkspaceRole.Viewer);
        _members.Verify(m => m.Update(It.IsAny<WorkspaceMember>()), Times.Never);
    }

    [Fact]
    public async Task A_membership_an_admin_deactivated_is_never_reactivated()
    {
        var ws = Guid.NewGuid();
        Saml(c => c.GroupRoleMappings = [new SsoGroupRoleMapping("Credit", ws, WorkspaceRole.Operator)]);
        var member = new WorkspaceMember { WorkspaceId = ws, OrgUserId = Guid.NewGuid(), Role = WorkspaceRole.Operator, IsActive = false };
        MemberSetup(member);

        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", ["Credit"]));

        member.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Leaving_the_idp_group_removes_access_sso_granted()
    {
        var ws = Guid.NewGuid();
        Saml(c => c.GroupRoleMappings = [new SsoGroupRoleMapping("Credit", ws, WorkspaceRole.Operator)]);
        var member = new WorkspaceMember { WorkspaceId = ws, OrgUserId = Guid.NewGuid(), Role = WorkspaceRole.Operator, IsActive = true, ManagedBySso = true };
        MemberSetup(member);

        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", ["Marketing"]));

        member.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Workspaces_no_mapping_names_are_left_alone()
    {
        var mapped = Guid.NewGuid();
        Saml(c => c.GroupRoleMappings = [new SsoGroupRoleMapping("Credit", mapped, WorkspaceRole.Operator)]);
        var other = new WorkspaceMember { WorkspaceId = Guid.NewGuid(), OrgUserId = Guid.NewGuid(), Role = WorkspaceRole.Designer, IsActive = true, ManagedBySso = true };
        var (user, _) = MemberSetup(other);
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Workspace { Id = other.WorkspaceId, OrgId = OrgId }, new Workspace { Id = mapped, OrgId = OrgId }]);

        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", []));

        other.IsActive.Should().BeTrue();
        other.Role.Should().Be(WorkspaceRole.Designer);
        _members.Verify(m => m.GetAsync(other.WorkspaceId, user.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Group_mappings_never_demote_a_workspaces_last_admin()
    {
        var ws = Guid.NewGuid();
        Saml(c => c.GroupRoleMappings = [new SsoGroupRoleMapping("Credit", ws, WorkspaceRole.Operator)]);
        var user = new OrgUser { Id = Guid.NewGuid(), OrgId = OrgId, Email = "jane@acme.test", IsActive = true };
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.test", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync([new Workspace { Id = ws, OrgId = OrgId }]);
        var member = new WorkspaceMember { WorkspaceId = ws, OrgUserId = user.Id, Role = WorkspaceRole.Admin, IsActive = true, ManagedBySso = true };
        _members.Setup(m => m.GetAsync(ws, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        var calls = new List<string>();
        _members.Setup(m => m.LockAdminsAsync(ws, It.IsAny<CancellationToken>())).Callback(() => calls.Add("lock")).Returns(Task.CompletedTask);
        _members.Setup(m => m.CountActiveAdminsAsync(ws, It.IsAny<CancellationToken>())).Callback(() => calls.Add("count")).ReturnsAsync(1);

        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", ["Credit"]));

        member.Role.Should().Be(WorkspaceRole.Admin);
        calls.Should().Equal("lock", "count"); // the count is taken under the workspace's lock, inside the transaction
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task With_scim_in_use_roles_come_from_scim_groups_not_the_sign_in()
    {
        var ws = Guid.NewGuid();
        Saml(c => c.GroupRoleMappings = [new SsoGroupRoleMapping("Credit", ws, WorkspaceRole.Operator)]);
        var member = new WorkspaceMember { WorkspaceId = ws, OrgUserId = Guid.NewGuid(), Role = WorkspaceRole.Operator, IsActive = true, ManagedBySso = true };
        var user = new OrgUser { Id = member.OrgUserId, OrgId = OrgId, Email = "jane@acme.test", IsActive = true };
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.test", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync([new Workspace { Id = ws, OrgId = OrgId }]);
        _members.Setup(m => m.GetAsync(ws, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        _scim.Setup(s => s.HasActiveTokenAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _scim.Setup(s => s.GetGroupNamesForUserAsync(OrgId, user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(["Credit"]);

        // The assertion has no groups; without SCIM that would remove the Operator membership.
        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", []));

        member.IsActive.Should().BeTrue();
        member.Role.Should().Be(WorkspaceRole.Operator);
    }

    // ── Identity checks ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_first_sso_sign_in_links_the_account_to_the_idp_subject()
    {
        Saml();
        var user = new OrgUser { Id = Guid.NewGuid(), OrgId = OrgId, Email = "jane@acme.test", IsActive = true };
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.test", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", Subject: "oidc|https://idp|u1"));

        user.SsoSubject.Should().Be("oidc|https://idp|u1");
    }

    [Fact]
    public async Task A_different_idp_subject_for_a_linked_account_is_refused()
    {
        Saml();
        var user = new OrgUser { Id = Guid.NewGuid(), OrgId = OrgId, Email = "jane@acme.test", IsActive = true, SsoSubject = "oidc|https://idp|u1" };
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.test", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var act = () => Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", Subject: "oidc|https://idp|attacker"));

        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_SUBJECT_MISMATCH");
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unverified_email_is_refused()
    {
        Saml();
        var act = () => Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", EmailVerified: false));
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_EMAIL_UNVERIFIED");
    }

    [Fact]
    public async Task Emails_outside_the_allowed_domains_are_refused()
    {
        Saml(c => c.AllowedEmailDomains = ["acme.com"]);
        NoUser("jane@gmail.com");
        var act = () => Service().ProvisionUserFromSsoAsync("jane@gmail.com", OrgId, new SsoClaims("jane@gmail.com", "Jane"));
        var refused = (await act.Should().ThrowAsync<SsoLoginException>()).Which;
        refused.ErrorCode.Should().Be("SSO_DOMAIN_NOT_ALLOWED");
        refused.Message.Should().Contain("gmail.com");
        _orgUsers.Verify(r => r.AddAsync(It.IsAny<OrgUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Allowed_domains_are_normalised_and_checked()
    {
        Saml();
        var dto = await Service().ConfigureAsync(OrgId, OrgSlug, new ConfigureSsoRequest("saml", true, "https://idp", "https://idp/sso", null, null, null, null, null,
            AllowedEmailDomains: ["@Acme.com", " acme.co.uk "]), Guid.NewGuid());
        dto.AllowedEmailDomains.Should().Equal("acme.com", "acme.co.uk");

        var act = () => Service().ConfigureAsync(OrgId, OrgSlug, new ConfigureSsoRequest("saml", true, "https://idp", "https://idp/sso", null, null, null, null, null,
            AllowedEmailDomains: ["not a domain"]), Guid.NewGuid());
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.Message.Should().Contain("isn't an email domain");
    }

    [Fact]
    public async Task An_http_issuer_url_is_refused()
    {
        Oidc();
        var act = () => Service().ConfigureAsync(OrgId, OrgSlug, new ConfigureSsoRequest("oidc", true, null, null, null, "http://169.254.169.254", null, null, null), Guid.NewGuid());
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.Message.Should().Contain("https");
    }

    [Fact]
    public async Task Org_owners_roles_are_never_changed_by_sso()
    {
        Saml(c => c.GroupRoleMappings = [new SsoGroupRoleMapping("Anyone", null, WorkspaceRole.Viewer)]);
        var owner = new OrgUser { Id = Guid.NewGuid(), OrgId = OrgId, Email = "jane@acme.test", IsActive = true, IsOrgOwner = true };
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.test", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);

        await Service().ProvisionUserFromSsoAsync("jane@acme.test", OrgId, new SsoClaims("jane@acme.test", "Jane", ["Anyone"]));

        _workspaces.Verify(w => w.GetForOrgAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── SAML flow ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Saml_login_stores_the_request_id_and_a_safe_return_path()
    {
        Saml();
        SsoLoginState? stored = null;
        _states.Setup(s => s.CreateAsync(It.IsAny<SsoLoginState>(), It.IsAny<CancellationToken>()))
            .Callback<SsoLoginState, CancellationToken>((s, _) => stored = s).ReturnsAsync("relay");
        string? requestId = null;
        _saml.Setup(s => s.BuildAuthnRequest("https://idp/sso", "https://app.flowamaz.io/saml/acme", "https://app.flowamaz.io/api/v1/saml/acme/acs",
                It.IsAny<string>(), "relay"))
            .Callback<string, string, string, string, string>((_, _, _, id, _) => requestId = id)
            .Returns(new SamlAuthnRequest("https://idp/sso?SAMLRequest=x", "ignored"));

        var redirect = await Service().InitiateSamlLoginAsync(OrgSlug, "https://evil.example/steal");

        redirect.StateKey.Should().Be("relay", "the controller binds this key to the browser");
        stored!.ReturnPath.Should().Be("/", "only on-site paths are kept");
        stored.SamlRequestId.Should().Be(requestId).And.StartWith("_");
    }

    [Fact]
    public async Task A_saml_response_to_another_request_is_refused()
    {
        Saml();
        SamlReturns(inResponseTo: "_other");
        _states.Setup(s => s.ConsumeAsync("relay", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoLoginState(OrgSlug, "saml", "/workflows", SamlRequestId: "_mine"));

        var act = () => Service().HandleSamlCallbackAsync(OrgSlug, "resp", "relay", Binding);
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_SIGNIN_EXPIRED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("someone-elses-binding")]
    public async Task A_saml_response_delivered_to_a_browser_that_didnt_start_the_sign_in_is_refused(string? binding)
    {
        Saml();
        NoUser();
        SamlReturns(inResponseTo: "_mine");
        _states.Setup(s => s.ConsumeAsync("relay", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoLoginState(OrgSlug, "saml", "/", SamlRequestId: "_mine"));

        var act = () => Service().HandleSamlCallbackAsync(OrgSlug, "resp", "relay", binding);
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_SIGNIN_EXPIRED");
        _orgUsers.Verify(r => r.AddAsync(It.IsAny<OrgUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_saml_response_to_this_request_signs_in_and_returns_to_the_saved_path()
    {
        Saml();
        NoUser();
        SamlReturns(inResponseTo: "_mine");
        _states.Setup(s => s.ConsumeAsync("relay", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoLoginState(OrgSlug, "saml", "/workflows", SamlRequestId: "_mine"));

        var result = await Service().HandleSamlCallbackAsync(OrgSlug, "resp", "relay", Binding);

        result.User.Email.Should().Be("jane@acme.test");
        result.ReturnPath.Should().Be("/workflows");
    }

    [Fact]
    public async Task An_idp_initiated_response_is_refused_by_default()
    {
        Saml();
        NoUser();
        SamlReturns(inResponseTo: null);
        var act = () => Service().HandleSamlCallbackAsync(OrgSlug, "resp", relayState: null, browserBinding: null);
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_IDP_INITIATED_OFF");
    }

    [Fact]
    public async Task An_idp_initiated_response_is_accepted_when_allowed_and_goes_home()
    {
        Saml(c => c.AllowIdpInitiated = true);
        NoUser();
        SamlReturns(inResponseTo: null);
        (await Service().HandleSamlCallbackAsync(OrgSlug, "resp", relayState: null, browserBinding: null)).ReturnPath.Should().Be("/");
    }

    [Fact]
    public async Task A_replayed_assertion_is_refused()
    {
        Saml(c => c.AllowIdpInitiated = true);
        SamlReturns(assertionId: "_used");
        _replay.Setup(r => r.TryRecordAsync(OrgSlug, "_used", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => Service().HandleSamlCallbackAsync(OrgSlug, "resp", null, null);
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_SIGNIN_EXPIRED");
        _orgUsers.Verify(r => r.AddAsync(It.IsAny<OrgUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_saml_check_uses_the_configured_idp_and_this_services_urls()
    {
        Saml(c => { c.GroupsAttribute = "memberOf"; c.AllowIdpInitiated = true; });
        NoUser();
        SamlExpectations? used = null;
        _saml.Setup(s => s.ValidateResponse("resp", It.IsAny<SamlExpectations>()))
            .Callback<string, SamlExpectations>((_, e) => used = e)
            .Returns(new SamlAssertion(new SsoClaims("jane@acme.test", "Jane"), "_a", DateTime.UtcNow.AddMinutes(5), null));

        await Service().HandleSamlCallbackAsync(OrgSlug, "resp", null, null);

        used.Should().Be(new SamlExpectations("decrypted", "https://idp", "https://app.flowamaz.io/saml/acme",
            "https://app.flowamaz.io/api/v1/saml/acme/acs", "memberOf"));
    }

    // ── OIDC flow ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_oidc_callback_with_an_unknown_state_is_refused()
    {
        Oidc();
        _states.Setup(s => s.ConsumeAsync("bad", It.IsAny<CancellationToken>())).ReturnsAsync((SsoLoginState?)null);
        var act = () => Service().HandleOidcCallbackAsync(OrgSlug, "code", "bad", SsoBrowserBinding.For("bad"));
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_SIGNIN_EXPIRED");
    }

    [Fact]
    public async Task An_oidc_callback_for_another_org_is_refused()
    {
        Oidc();
        _states.Setup(s => s.ConsumeAsync("st", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoLoginState("other-org", "oidc", "/", OidcNonce: "n", OidcCodeVerifier: "v"));
        var act = () => Service().HandleOidcCallbackAsync(OrgSlug, "code", "st", SsoBrowserBinding.For("st"));
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_SIGNIN_EXPIRED");
    }

    [Fact]
    public async Task An_oidc_callback_passes_the_saved_nonce_and_verifier()
    {
        Oidc();
        NoUser();
        var discovery = new OidcDiscovery("https://idp", "https://idp/a", "https://idp/t", "https://idp/k");
        _oidc.Setup(o => o.DiscoverAsync("https://idp", It.IsAny<CancellationToken>())).ReturnsAsync(discovery);
        _states.Setup(s => s.ConsumeAsync("st", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoLoginState(OrgSlug, "oidc", "/runs", OidcNonce: "n-1", OidcCodeVerifier: "v-1"));
        _oidc.Setup(o => o.ExchangeCodeAsync(discovery, "c", "decrypted", "https://app.flowamaz.io/api/v1/oidc/acme/callback",
                "code", "v-1", "n-1", "groups", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoClaims("jane@acme.test", "Jane"));

        var result = await Service().HandleOidcCallbackAsync(OrgSlug, "code", "st", SsoBrowserBinding.For("st"));

        result.ReturnPath.Should().Be("/runs");
    }

    [Fact]
    public async Task An_oidc_callback_in_a_browser_that_didnt_start_the_sign_in_is_refused()
    {
        Oidc();
        _states.Setup(s => s.ConsumeAsync("st", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoLoginState(OrgSlug, "oidc", "/", OidcNonce: "n", OidcCodeVerifier: "v"));
        var act = () => Service().HandleOidcCallbackAsync(OrgSlug, "code", "st", browserBinding: null);
        (await act.Should().ThrowAsync<SsoLoginException>()).Which.ErrorCode.Should().Be("SSO_SIGNIN_EXPIRED");
        _oidc.Verify(o => o.ExchangeCodeAsync(It.IsAny<OidcDiscovery>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Require SSO ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Require_sso_blocks_passwords_for_members_but_not_org_owners()
    {
        Saml(c => c.RequireSso = true);
        var service = Service();
        (await service.IsPasswordLoginBlockedAsync(OrgId, new OrgUser { IsOrgOwner = false })).Should().BeTrue();
        (await service.IsPasswordLoginBlockedAsync(OrgId, new OrgUser { IsOrgOwner = true })).Should().BeFalse();
    }

    [Fact]
    public async Task Turning_require_sso_on_ends_non_owner_sessions_once()
    {
        Saml();
        _refreshTokens.Setup(r => r.RevokeAllForOrgNonOwnersAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var on = new ConfigureSsoRequest("saml", true, "https://idp", "https://idp/sso", null, null, null, null, null, RequireSso: true);

        await Service().ConfigureAsync(OrgId, OrgSlug, on, Guid.NewGuid());
        await Service().ConfigureAsync(OrgId, OrgSlug, on, Guid.NewGuid()); // already on: nothing more to revoke

        _refreshTokens.Verify(r => r.RevokeAllForOrgNonOwnersAsync(OrgId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Require_sso_has_no_effect_when_the_plan_lacks_sso()
    {
        Saml(c => c.RequireSso = true);
        _enterprise = false;
        (await Service().IsPasswordLoginBlockedAsync(OrgId, new OrgUser())).Should().BeFalse();
    }

    // ── Check settings ────────────────────────────────────────────────────────────────

    private static string CertPem(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=idp", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(notBefore, notAfter);
        return cert.ExportCertificatePem();
    }

    [Fact]
    public async Task Check_settings_contacts_the_oidc_idp_and_reports_a_typo_in_the_issuer()
    {
        Oidc();
        _oidc.Setup(o => o.DiscoverAsync("https://idp", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SsoLoginException("SSO_IDP_UNREACHABLE", "Flowamaz couldn't read your identity provider's OpenID configuration. Check the issuer URL.", 502));

        var result = await Service().TestAsync(OrgId);

        result.Ok.Should().BeFalse();
        result.Message.Should().Contain("issuer URL");
    }

    [Fact]
    public async Task Check_settings_reports_the_keys_found_and_that_sso_is_already_on()
    {
        Oidc();
        var discovery = new OidcDiscovery("https://idp", "https://idp/a", "https://idp/t", "https://idp/k");
        _oidc.Setup(o => o.DiscoverAsync("https://idp", It.IsAny<CancellationToken>())).ReturnsAsync(discovery);
        _oidc.Setup(o => o.CountSigningKeysAsync(discovery, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var result = await Service().TestAsync(OrgId);

        result.Ok.Should().BeTrue();
        result.Message.Should().Contain("2 signing keys").And.Contain("SSO is on").And.NotContain("ready to activate");
    }

    [Fact]
    public async Task Check_settings_refuses_an_expired_saml_certificate()
    {
        Saml();
        _protector.Setup(p => p.Unprotect(OrgId, "enc")).Returns(CertPem(DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddDays(-1)));

        var result = await Service().TestAsync(OrgId);

        result.Ok.Should().BeFalse();
        result.Message.Should().Contain("expired");
    }

    [Fact]
    public async Task Check_settings_accepts_a_valid_saml_setup_and_asks_for_a_test_sign_in()
    {
        Saml(c => c.IsActive = false);
        _protector.Setup(p => p.Unprotect(OrgId, "enc")).Returns(CertPem(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1)));

        var result = await Service().TestAsync(OrgId);

        result.Ok.Should().BeTrue();
        result.Message.Should().Contain("private window").And.Contain("Turn on SSO");
    }

    [Fact]
    public async Task Check_settings_names_what_is_missing()
    {
        Saml(c => c.EncryptedIdpCertificate = null);
        (await Service().TestAsync(OrgId)).Message.Should().Contain("certificate");
    }

    // ── Return paths ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/workflows/1?tab=runs", "/workflows/1?tab=runs")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example/x", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("javascript:alert(1)", "/")]
    public void Return_paths_stay_on_site(string? input, string expected) =>
        SsoService.SafeReturnPath(input).Should().Be(expected);
}
