using Flowamaz.Application.Auth.Services;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Auth;

public class SsoServiceTests
{
    private readonly Mock<IOrgSsoConfigRepository> _configs = new();
    private readonly Mock<IOrganisationRepository> _orgs = new();
    private readonly Mock<IOrgUserRepository> _orgUsers = new();
    private readonly Mock<IWorkspaceRepository> _workspaces = new();
    private readonly Mock<IWorkspaceMemberRepository> _members = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<ISecretProtector> _protector = new();
    private readonly Mock<ISamlProcessor> _saml = new();
    private readonly Mock<IOidcClient> _oidc = new();
    private readonly Mock<IOidcStateStore> _stateStore = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string OrgSlug = "acme";

    public SsoServiceTests()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _members.Setup(m => m.GetActiveMembershipsForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<OrgUser>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkspaceMembership>>()))
            .Returns("jwt-token");
        _protector.Setup(p => p.Unprotect(It.IsAny<Guid>(), It.IsAny<string>())).Returns("decrypted");
    }

    private SsoService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:BaseUrl"] = "https://app.flowamaz.io" })
            .Build();
        return new SsoService(
            _configs.Object, _orgs.Object, _orgUsers.Object, _workspaces.Object, _members.Object,
            _jwt.Object, _protector.Object, _saml.Object, _oidc.Object, _stateStore.Object,
            _uow.Object, config, NullLogger<SsoService>.Instance);
    }

    private void SetupActiveOrg(string provider)
    {
        _orgs.Setup(o => o.GetBySlugAsync(OrgSlug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organisation { Id = OrgId, Slug = OrgSlug });
        _configs.Setup(c => c.GetByOrgIdAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgSsoConfig
            {
                OrgId = OrgId, Provider = provider, IsActive = true,
                IdpSsoUrl = "https://idp.example/sso", SpEntityId = "https://app.flowamaz.io/saml/acme",
                EncryptedIdpCertificate = "enc-cert",
                IssuerUrl = "https://idp.example", ClientId = "client", EncryptedClientSecret = "enc-secret",
            });
    }

    [Fact]
    public async Task ProvisionUserFromSso_creates_new_user_with_viewer_role()
    {
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("new@acme.com", OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrgUser?)null);
        _workspaces.Setup(w => w.GetForOrgAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Workspace { Id = Guid.NewGuid(), OrgId = OrgId }]);
        WorkspaceMember? assigned = null;
        _members.Setup(m => m.AddAsync(It.IsAny<WorkspaceMember>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceMember, CancellationToken>((m, _) => assigned = m)
            .Returns(Task.CompletedTask);

        var user = await CreateService().ProvisionUserFromSsoAsync("new@acme.com", OrgId, new SsoClaims("new@acme.com", "New User"));

        user.IsSsoProvisioned.Should().BeTrue();
        user.Name.Should().Be("New User");
        _orgUsers.Verify(r => r.AddAsync(It.IsAny<OrgUser>(), It.IsAny<CancellationToken>()), Times.Once);
        assigned.Should().NotBeNull();
        assigned!.Role.Should().Be(Core.Enums.WorkspaceRole.Viewer);
    }

    [Fact]
    public async Task ProvisionUserFromSso_updates_existing_user_without_creating()
    {
        var existing = new OrgUser { Id = Guid.NewGuid(), OrgId = OrgId, Email = "jane@acme.com", Name = "Old Name" };
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("jane@acme.com", OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var user = await CreateService().ProvisionUserFromSsoAsync("jane@acme.com", OrgId, new SsoClaims("jane@acme.com", "Jane New"));

        user.Id.Should().Be(existing.Id);
        user.Name.Should().Be("Jane New");
        _orgUsers.Verify(r => r.AddAsync(It.IsAny<OrgUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSamlCallback_invalid_signature_throws()
    {
        SetupActiveOrg("saml");
        _saml.Setup(s => s.ValidateResponse(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("SAML signature validation failed"));

        var act = () => CreateService().HandleSamlCallbackAsync(OrgSlug, "bad-response");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*signature*");
    }

    [Fact]
    public async Task HandleOidcCallback_valid_code_returns_user_and_token()
    {
        SetupActiveOrg("oidc");
        _stateStore.Setup(s => s.ConsumeAsync("state-1", It.IsAny<CancellationToken>())).ReturnsAsync(OrgSlug);
        _oidc.Setup(o => o.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), "code-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SsoClaims("sso@acme.com", "SSO User"));
        _orgUsers.Setup(r => r.GetByEmailAndOrgAsync("sso@acme.com", OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrgUser?)null);

        var result = await CreateService().HandleOidcCallbackAsync(OrgSlug, "code-1", "state-1");

        result.User.Email.Should().Be("sso@acme.com");
        result.AccessToken.Should().Be("jwt-token");
    }

    [Fact]
    public async Task HandleOidcCallback_invalid_state_throws_unauthorized()
    {
        _stateStore.Setup(s => s.ConsumeAsync("bad-state", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var act = () => CreateService().HandleOidcCallbackAsync(OrgSlug, "code-1", "bad-state");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
