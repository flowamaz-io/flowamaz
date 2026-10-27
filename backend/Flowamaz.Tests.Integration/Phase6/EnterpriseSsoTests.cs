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
/// in the URL); the response only works in the browser that started the sign-in (login CSRF); IdP-initiated sign-in
/// is off unless allowed, and a replayed response is refused; "require SSO" blocks member passwords and ends their
/// sessions, but not the owner's; SSO only changes memberships it created; and SSO is Enterprise-only.
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

    private async Task<Owner> EnterpriseOrgWithSamlAsync(string slug, bool requireSso = false, bool allowIdpInitiated = false, object? mappings = null)
    {
        var owner = await RegisterOwnerAsync(slug);
        await CreateWorkspaceAsync(owner.Client, "SSO", $"{slug}-ws");
        await SetPlanAsync(owner.OrgId, PlatformSeedData.EnterprisePlanId);
        var put = await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "saml", isActive = true, idpEntityId = SamlTestIdp.IdpEntityId, idpSsoUrl = "https://idp.acme.test/sso",
            idpCertificate = _idp.CertificatePem, requireSso, allowIdpInitiated, groupRoleMappings = mappings,
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
        var replay = await PostAcsAsync(browser, slug, response, relay);
        replay.StatusCode.Should().Be(HttpStatusCode.Redirect);
        replay.Headers.Location!.ToString().Should().Contain("/login?org=ent-sso-flow&sso_error=SSO_SIGNIN_EXPIRED");
    }

    [Fact]
    public async Task A_response_posted_to_another_browser_is_refused()
    {
        // Login CSRF: the attacker starts a sign-in with their own account and makes the victim's browser post it.
        const string slug = "ent-sso-csrf";
        await EnterpriseOrgWithSamlAsync(slug);
        var attackerBrowser = Fixture.NewClientWithoutRedirects();
        var (relay, requestId) = await StartSamlAsync(attackerBrowser, slug);
        var response = _idp.Build(new() { Email = "attacker@ent-sso-csrf.test", InResponseTo = requestId, Audience = Sp(slug), Recipient = Acs(slug) });

        var victimBrowser = Fixture.NewClientWithoutRedirects();
        var acs = await PostAcsAsync(victimBrowser, slug, response, relay);

        acs.Headers.Location!.ToString().Should().Contain("sso_error=SSO_SIGNIN_EXPIRED");
        acs.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).Should().NotContain(c => c.StartsWith("fmz_refresh=") && !c.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task Idp_initiated_sign_in_is_off_by_default_and_replays_are_refused_when_on()
    {
        const string slug = "ent-sso-idp-init";
        var owner = await EnterpriseOrgWithSamlAsync(slug);
        var unsolicited = _idp.Build(new() { Email = "tile@ent-sso-idp-init.test", Audience = Sp(slug), Recipient = Acs(slug) });

        var off = await PostAcsAsync(Fixture.NewClientWithoutRedirects(), slug, unsolicited, null);
        off.Headers.Location!.ToString().Should().Contain("sso_error=SSO_IDP_INITIATED_OFF");

        var put = await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "saml", isActive = true, idpEntityId = SamlTestIdp.IdpEntityId, idpSsoUrl = "https://idp.acme.test/sso", allowIdpInitiated = true,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var fresh = _idp.Build(new() { Email = "tile@ent-sso-idp-init.test", Audience = Sp(slug), Recipient = Acs(slug) });

        var on = await PostAcsAsync(Fixture.NewClientWithoutRedirects(), slug, fresh, null);
        on.Headers.Location!.ToString().Should().Be($"{App}/");
        var replay = await PostAcsAsync(Fixture.NewClientWithoutRedirects(), slug, fresh, null);
        replay.Headers.Location!.ToString().Should().Contain("sso_error=SSO_SIGNIN_EXPIRED");
    }

    [Fact]
    public async Task Group_mappings_only_change_memberships_sso_created()
    {
        const string slug = "ent-sso-groups";
        var owner = await RegisterOwnerAsync(slug);
        var ws = await CreateWorkspaceAsync(owner.Client, "Lending", $"{slug}-ws");
        await SetPlanAsync(owner.OrgId, PlatformSeedData.EnterprisePlanId);
        var put = await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "saml", isActive = true, idpEntityId = SamlTestIdp.IdpEntityId, idpSsoUrl = "https://idp.acme.test/sso",
            idpCertificate = _idp.CertificatePem, groupRoleMappings = new[] { new { group = "Credit", workspaceId = ws, role = "Operator" } },
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        async Task SignInAsync(string email, params string[] groups)
        {
            var browser = Fixture.NewClientWithoutRedirects();
            var (relay, requestId) = await StartSamlAsync(browser, slug);
            var acs = await PostAcsAsync(browser, slug,
                _idp.Build(new() { Email = email, Groups = groups, InResponseTo = requestId, Audience = Sp(slug), Recipient = Acs(slug) }), relay);
            acs.Headers.Location!.ToString().Should().NotContain("sso_error");
        }

        async Task<Core.Entities.Workspaces.WorkspaceMember?> MemberAsync(string email)
        {
            using var scope = Fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var user = await db.OrgUsers.SingleAsync(u => u.Email == email && u.OrgId == owner.OrgId);
            return await db.WorkspaceMembers.AsNoTracking().SingleOrDefaultAsync(m => m.WorkspaceId == ws && m.OrgUserId == user.Id);
        }

        await SignInAsync("analyst@ent-sso-groups.test", "Credit");
        (await MemberAsync("analyst@ent-sso-groups.test")).Should().Match<Core.Entities.Workspaces.WorkspaceMember>(m =>
            m.IsActive && m.ManagedBySso && m.Role == Core.Enums.WorkspaceRole.Operator);

        await SignInAsync("analyst@ent-sso-groups.test", "Marketing"); // left the group
        (await MemberAsync("analyst@ent-sso-groups.test"))!.IsActive.Should().BeFalse();

        // An admin hand-sets a role: SSO leaves it alone from then on.
        await SignInAsync("analyst@ent-sso-groups.test", "Credit");
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var user = await db.OrgUsers.SingleAsync(u => u.Email == "analyst@ent-sso-groups.test");
            await db.WorkspaceMembers.Where(m => m.WorkspaceId == ws && m.OrgUserId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, Core.Enums.WorkspaceRole.Designer).SetProperty(m => m.ManagedBySso, false));
        }
        await SignInAsync("analyst@ent-sso-groups.test", "Marketing");
        (await MemberAsync("analyst@ent-sso-groups.test")).Should().Match<Core.Entities.Workspaces.WorkspaceMember>(m =>
            m.IsActive && !m.ManagedBySso && m.Role == Core.Enums.WorkspaceRole.Designer);
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

        var reset = await Fixture.NewClient().PostAsJsonAsync("/api/v1/auth/forgot-password",
            new { email = "member@ent-sso-required.test", orgSlug = slug });
        reset.StatusCode.Should().Be(HttpStatusCode.OK, "same answer as for any address");
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            var memberId = await db.OrgUsers.Where(u => u.Email == "member@ent-sso-required.test").Select(u => u.Id).SingleAsync();
            (await db.PasswordResetTokens.AnyAsync(t => t.OrgUserId == memberId)).Should().BeFalse("a password would get around the org's SSO");
        }

        var status = await Fixture.NewClient().GetAsync($"/api/v1/auth/sso/{slug}");
        (await status.Content.ReadAsStringAsync()).Should().Contain("\"required\":true");
    }

    [Fact]
    public async Task Turning_require_sso_on_ends_member_sessions_but_not_the_owners()
    {
        const string slug = "ent-sso-revoke";
        var owner = await EnterpriseOrgWithSamlAsync(slug);
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.OrgUsers.Add(new OrgUser
            {
                OrgId = owner.OrgId, Email = "member@ent-sso-revoke.test", Name = "Member",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Sup3rSecret!", 4), IsActive = true,
            });
            await db.SaveChangesAsync();
        }
        var memberBrowser = Fixture.NewClient();
        (await memberBrowser.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "member@ent-sso-revoke.test", password = "Sup3rSecret!", orgSlug = slug })).StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerBrowser = Fixture.NewClient();
        (await ownerBrowser.PostAsJsonAsync("/api/v1/auth/login",
            new { email = owner.Email, password = "Sup3rSecret!", orgSlug = slug })).StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "saml", isActive = true, idpEntityId = SamlTestIdp.IdpEntityId, idpSsoUrl = "https://idp.acme.test/sso", requireSso = true,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        (await memberBrowser.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ownerBrowser.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);
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
