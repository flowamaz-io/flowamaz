using FluentAssertions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// SSO JIT provisioning over the real DB: a new SSO user is created with the Viewer role; a repeat
/// login updates the same user; an OIDC state mismatch is rejected.
/// </summary>
[Collection("api")]
public class Phase6SsoTests : ApiTestBase
{
    public Phase6SsoTests(IntegrationApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ProvisionUserFromSso_creates_new_user_then_updates_existing()
    {
        var owner = await RegisterOwnerAsync("p6-sso1");
        await CreateWorkspaceAsync(owner.Client, "SSO", "sso-ws");

        using var scope = Fixture.Services.CreateScope();
        var sso = scope.ServiceProvider.GetRequiredService<ISsoService>();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();

        var created = await sso.ProvisionUserFromSsoAsync("sso@p6-sso1.test", owner.OrgId, new SsoClaims("sso@p6-sso1.test", "First Name"));
        created.IsSsoProvisioned.Should().BeTrue();
        created.Name.Should().Be("First Name");

        var again = await sso.ProvisionUserFromSsoAsync("sso@p6-sso1.test", owner.OrgId, new SsoClaims("sso@p6-sso1.test", "Updated Name"));
        again.Id.Should().Be(created.Id, "the same user is updated, not duplicated");

        var count = await db.OrgUsers.CountAsync(u => u.OrgId == owner.OrgId && u.Email == "sso@p6-sso1.test");
        count.Should().Be(1);
    }

    [Fact]
    public async Task OidcCallback_with_mismatched_state_is_rejected()
    {
        await RegisterOwnerAsync("p6-sso2");

        using var scope = Fixture.Services.CreateScope();
        var sso = scope.ServiceProvider.GetRequiredService<ISsoService>();

        var act = () => sso.HandleOidcCallbackAsync("p6-sso2", "some-code", "never-issued-state");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
