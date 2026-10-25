using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// An organisation owner can only see and change their own organisation's SSO. Before the fix, the owner of any
/// organisation could point another organisation's SSO at their own IdP and sign in as its users.
/// </summary>
[Collection("api")]
public class SsoOrgScopeTests : ApiTestBase
{
    public SsoOrgScopeTests(IntegrationApiFixture fixture) : base(fixture) { }

    private static object SamlConfig(string idp) => new
    {
        provider = "saml", isActive = true, idpEntityId = idp, idpSsoUrl = $"{idp}/sso",
        idpCertificate = "MIIB-test", issuerUrl = (string?)null, clientId = (string?)null, clientSecret = (string?)null, scopes = (string[]?)null,
    };

    [Fact]
    public async Task Another_organisations_owner_cannot_read_change_test_or_disable_its_sso()
    {
        var victim = await RegisterOwnerAsync("sso-victim");
        var attacker = await RegisterOwnerAsync("sso-attacker");
        (await victim.Client.PutAsJsonAsync($"/api/v1/organisations/{victim.OrgId}/sso", SamlConfig("https://idp.victim.test")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var url = $"/api/v1/organisations/{victim.OrgId}/sso";
        (await attacker.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await attacker.Client.PutAsJsonAsync(url, SamlConfig("https://idp.attacker.test"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await attacker.Client.PostAsync($"{url}/test", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await attacker.Client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var config = await db.Set<Flowamaz.Core.Entities.Auth.OrgSsoConfig>().AsNoTracking().SingleAsync(c => c.OrgId == victim.OrgId);
        config.IdpEntityId.Should().Be("https://idp.victim.test", "the attacker's IdP must not replace the victim's");
        config.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task An_owner_can_still_manage_their_own_organisations_sso()
    {
        var owner = await RegisterOwnerAsync("sso-own");
        var url = $"/api/v1/organisations/{owner.OrgId}/sso";
        (await owner.Client.PutAsJsonAsync(url, SamlConfig("https://idp.own.test"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.Client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
