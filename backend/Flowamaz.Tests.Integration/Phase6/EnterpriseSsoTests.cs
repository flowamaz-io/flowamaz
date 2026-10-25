using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Seed;
using Flowamaz.Tests.Integration.Common;
using Flowamaz.Tests.Unit.Auth.Sso;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// Enterprise SSO over the real server: SP-initiated SAML sign-in ends in a normal session (refresh cookie, no token
/// in the URL); a replayed response is refused; "require SSO" blocks member passwords but not the owner's; and SSO
/// is Enterprise-only.
/// </summary>
[Collection("api")]
public sealed class EnterpriseSsoTests : ApiTestBase, IDisposable
{
    private const string App = "https://app.flowamaz.io";
    private readonly SamlTestIdp _idp = new();

    public EnterpriseSsoTests(IntegrationApiFixture fixture) : base(fixture) { }

    public void Dispose() => _idp.Dispose();

    private static string Sp(string slug) => $"{App}/saml/{slug}";
    private static string Acs(string slug) => $"{App}/api/v1/saml/{slug}/acs";

    private async Task SetPlanAsync(Guid orgId, Guid planId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        await db.Organisations.Where(o => o.Id == orgId).ExecuteUpdateAsync(s => s.SetProperty(o => o.PlanId, planId));
    }

    private async Task<Owner> EnterpriseOrgWithSamlAsync(string slug, bool requireSso = false)
    {
        var owner = await RegisterOwnerAsync(slug);
        await CreateWorkspaceAsync(owner.Client, "SSO", $"{slug}-ws");
        await SetPlanAsync(owner.OrgId, PlatformSeedData.EnterprisePlanId);
        var put = await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "saml", isActive = true, idpEntityId = SamlTestIdp.IdpEntityId, idpSsoUrl = "https://idp.acme.test/sso",
            idpCertificate = _idp.CertificatePem, requireSso,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        return owner;
    }

    private async Task<(string RelayState, string RequestId)> StartSamlAsync(HttpClient browser, string slug, string returnUrl = "/workflows")
    {
        var start = await browser.GetAsync($"/api/v1/saml/{slug}/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        start.StatusCode.Should().Be(HttpStatusCode.Redirect, await start.Content.ReadAsStringAsync());
        var query = HttpUtility.ParseQueryString(start.Headers.Location!.Query);
        var xml = Inflate(query["SAMLRequest"]!);
        var id = System.Text.RegularExpressions.Regex.Match(xml, "ID=\"([^\"]+)\"").Groups[1].Value;
        return (query["RelayState"]!, id);
    }

    private static Task<HttpResponseMessage> PostAcsAsync(HttpClient browser, string slug, string response, string? relayState) =>
        browser.PostAsync($"/api/v1/saml/{slug}/acs", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = response,
            ["RelayState"] = relayState ?? "",
        }));

    [Fact]
    public async Task Saml_sign_in_starts_a_session_and_cannot_be_replayed()
    {
        const string slug = "ent-sso-flow";
        await EnterpriseOrgWithSamlAsync(slug);
        var browser = Fixture.NewClientWithoutRedirects();

        var (relay, requestId) = await StartSamlAsync(browser, slug);
        var response = _idp.Build(new()
        {
            Email = "new.person@ent-sso-flow.test", InResponseTo = requestId, Audience = Sp(slug), Recipient = Acs(slug),
        });

        var acs = await PostAcsAsync(browser, slug, response, relay);
        acs.StatusCode.Should().Be(HttpStatusCode.Redirect);
        acs.Headers.Location!.ToString().Should().Be($"{App}/workflows", "back to where sign-in started, with no token in the URL");
        acs.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("fmz_refresh=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        // The cookie works like a password session.
        var refresh = await browser.PostAsync("/api/v1/auth/refresh", null);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK, await refresh.Content.ReadAsStringAsync());
        var token = (await DataAsync(refresh)).GetProperty("accessToken").GetString();
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Authorization = new("Bearer", token);
        var meBody = await DataAsync(await browser.SendAsync(me));
        meBody.GetProperty("email").GetString().Should().Be("new.person@ent-sso-flow.test");

        // The same response again: refused, and sent back to the login page with a code.
        var replay = await PostAcsAsync(Fixture.NewClientWithoutRedirects(), slug, response, relay);
        replay.StatusCode.Should().Be(HttpStatusCode.Redirect);
        replay.Headers.Location!.ToString().Should().Contain("/login?org=ent-sso-flow&sso_error=SSO_SIGNIN_EXPIRED");
    }

    [Fact]
    public async Task A_forged_response_is_refused_and_creates_no_account()
    {
        const string slug = "ent-sso-forged";
        await EnterpriseOrgWithSamlAsync(slug);
        var browser = Fixture.NewClientWithoutRedirects();
        var (relay, requestId) = await StartSamlAsync(browser, slug);
        using var attacker = new SamlTestIdp();
        var forged = attacker.Build(new() { Email = "ceo@ent-sso-forged.test", InResponseTo = requestId, Audience = Sp(slug), Recipient = Acs(slug) });

        var acs = await PostAcsAsync(browser, slug, forged, relay);

        acs.Headers.Location!.ToString().Should().Contain("sso_error=SSO_RESPONSE_REJECTED");
        using var scope = Fixture.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>().OrgUsers.AnyAsync(u => u.Email == "ceo@ent-sso-forged.test"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Require_sso_blocks_member_passwords_but_not_the_owners()
    {
        const string slug = "ent-sso-required";
        var owner = await EnterpriseOrgWithSamlAsync(slug, requireSso: true);
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.OrgUsers.Add(new OrgUser
            {
                OrgId = owner.OrgId, Email = "member@ent-sso-required.test", Name = "Member",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Sup3rSecret!", 4), IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var member = await Fixture.NewClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "member@ent-sso-required.test", password = "Sup3rSecret!", orgSlug = slug });
        member.StatusCode.Should().Be(HttpStatusCode.Forbidden, await member.Content.ReadAsStringAsync());
        (await member.Content.ReadAsStringAsync()).Should().Contain("SSO_REQUIRED");

        var wrongPassword = await Fixture.NewClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "member@ent-sso-required.test", password = "wrong-password", orgSlug = slug });
        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a wrong password still looks like a wrong password");

        var ownerLogin = await Fixture.NewClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = owner.Email, password = "Sup3rSecret!", orgSlug = slug });
        ownerLogin.StatusCode.Should().Be(HttpStatusCode.OK, "owners keep password sign-in so a broken IdP can't lock the org out");

        var status = await Fixture.NewClient().GetAsync($"/api/v1/auth/sso/{slug}");
        (await status.Content.ReadAsStringAsync()).Should().Contain("\"required\":true");
    }

    [Fact]
    public async Task Sso_is_enterprise_only()
    {
        var owner = await RegisterOwnerAsync("pro-no-sso");
        await SetPlanAsync(owner.OrgId, PlatformSeedData.ProPlanId);

        var put = await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "saml", isActive = true, idpEntityId = SamlTestIdp.IdpEntityId, idpSsoUrl = "https://idp/sso", idpCertificate = _idp.CertificatePem,
        });

        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await put.Content.ReadAsStringAsync()).Should().Contain("Enterprise");
        var get = await owner.Client.GetAsync($"/api/v1/organisations/{owner.OrgId}/sso");
        (await DataAsync(get)).GetProperty("planIncludesSso").GetBoolean().Should().BeFalse();
    }

    private static string Inflate(string base64)
    {
        using var inflate = new System.IO.Compression.DeflateStream(new MemoryStream(Convert.FromBase64String(base64)),
            System.IO.Compression.CompressionMode.Decompress);
        return new StreamReader(inflate).ReadToEnd();
    }
}
