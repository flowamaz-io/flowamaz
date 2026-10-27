using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Seed;
using Flowamaz.Tests.Integration.Common;
using Flowamaz.Tests.Unit.Auth.Sso;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// SCIM 2.0 over the real server, driven the way Okta and Microsoft Entra drive it: tokens (shown once, revocable,
/// Enterprise-only, one organisation each), the user lifecycle (match by userName, create, deactivate, reactivate,
/// delete, create again), owners protected from SCIM, groups whose names feed the SSO role mappings, and SSO sign-in
/// taking roles from SCIM groups once SCIM is in use.
/// </summary>
[Collection("api")]
public sealed class EnterpriseScimTests : ApiTestBase, IDisposable
{
    private const string App = "https://app.flowamaz.io";
    private readonly SamlTestIdp _idp = new();

    public EnterpriseScimTests(IntegrationApiFixture fixture) : base(fixture) { }

    public void Dispose() => _idp.Dispose();

    private async Task SetPlanAsync(Guid orgId, Guid planId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        await db.Organisations.Where(o => o.Id == orgId).ExecuteUpdateAsync(s => s.SetProperty(o => o.PlanId, planId));
    }

    private async Task<(Owner Owner, HttpClient Scim, string Token)> EnterpriseWithScimAsync(string slug)
    {
        var owner = await RegisterOwnerAsync(slug);
        await SetPlanAsync(owner.OrgId, PlatformSeedData.EnterprisePlanId);
        var created = await owner.Client.PostAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/scim/tokens", new { name = "Okta" });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var token = (await DataAsync(created)).GetProperty("plainToken").GetString()!;
        return (owner, ScimClient(token), token);
    }

    private HttpClient ScimClient(string token)
    {
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/scim+json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> CreateUserAsync(HttpClient scim, string email, string? externalId = null, bool active = true)
    {
        var response = await scim.PostAsync("/api/scim/v2/Users", Json(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = email,
            externalId = externalId ?? "ext-" + email,
            name = new { givenName = "Ana", familyName = "Lyst" },
            emails = new[] { new { value = email, type = "work", primary = true } },
            active,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadAsync(response)).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient scim, string resource, params object[] operations) =>
        scim.PatchAsync($"/api/scim/v2/{resource}", Json(new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" },
            Operations = operations,
        }));

    private async Task<T> DbAsync<T>(Func<FlowAmazDbContext, Task<T>> query)
    {
        using var scope = Fixture.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>());
    }

    // ── Tokens ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_token_is_shown_once_works_for_its_organisation_and_stops_when_revoked()
    {
        var (owner, scim, token) = await EnterpriseWithScimAsync("scim-token");
        token.Should().StartWith("fmzscim_");

        var list = await DataAsync(await owner.Client.GetAsync($"/api/v1/organisations/{owner.OrgId}/scim"));
        list.GetProperty("scimBaseUrl").GetString().Should().EndWith("/api/scim/v2");
        var listed = list.GetProperty("tokens")[0];
        listed.GetProperty("tokenPrefix").GetString().Should().Be(token[..14]);
        list.ToString().Should().NotContain(token, "the token itself is never shown again");
        (await DbAsync(db => db.ScimTokens.AnyAsync(t => t.TokenHash == token))).Should().BeFalse("only a hash is stored");

        var config = await scim.GetAsync("/api/scim/v2/ServiceProviderConfig");
        config.StatusCode.Should().Be(HttpStatusCode.OK);
        config.Content.Headers.ContentType!.MediaType.Should().Be("application/scim+json");
        (await ReadAsync(config)).GetProperty("patch").GetProperty("supported").GetBoolean().Should().BeTrue();

        var revoke = await owner.Client.DeleteAsync($"/api/v1/organisations/{owner.OrgId}/scim/tokens/{listed.GetProperty("id").GetGuid()}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var refused = await scim.GetAsync("/api/scim/v2/Users");
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadAsync(refused)).GetProperty("schemas")[0].GetString().Should().Be("urn:ietf:params:scim:api:messages:2.0:Error");
    }

    [Fact]
    public async Task Scim_is_enterprise_only()
    {
        var owner = await RegisterOwnerAsync("scim-pro");
        await SetPlanAsync(owner.OrgId, PlatformSeedData.ProPlanId);
        var created = await owner.Client.PostAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/scim/tokens", new { name = "Okta" });
        created.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A token made on Enterprise stops working if the organisation moves to a plan without SSO.
        var (ent, scim, _) = await EnterpriseWithScimAsync("scim-downgrade");
        await SetPlanAsync(ent.OrgId, PlatformSeedData.ProPlanId);
        (await scim.GetAsync("/api/scim/v2/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_only_sees_its_own_organisation()
    {
        var (_, scimA, _) = await EnterpriseWithScimAsync("scim-org-a");
        var (_, scimB, _) = await EnterpriseWithScimAsync("scim-org-b");
        var userInA = await CreateUserAsync(scimA, "person@scim-org-a.test");

        (await scimB.GetAsync($"/api/scim/v2/Users/{userInA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var filtered = await ReadAsync(await scimB.GetAsync(
            "/api/scim/v2/Users?filter=" + HttpUtility.UrlEncode("userName eq \"person@scim-org-a.test\"")));
        filtered.GetProperty("totalResults").GetInt32().Should().Be(0);
        (await PatchAsync(scimB, $"Users/{userInA}", new { op = "replace", path = "active", value = false }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DbAsync(db => db.OrgUsers.SingleAsync(u => u.Id == Guid.Parse(userInA)))).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Requests_without_a_valid_token_are_refused()
    {
        var anonymous = Fixture.NewClient();
        (await anonymous.GetAsync("/api/scim/v2/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // A user's own session token isn't a SCIM token.
        var owner = await RegisterOwnerAsync("scim-jwt");
        (await owner.Client.GetAsync("/api/scim/v2/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ScimClient("fmzscim_" + new string('x', 43)).GetAsync("/api/scim/v2/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Users ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Okta_style_user_lifecycle()
    {
        var (owner, scim, _) = await EnterpriseWithScimAsync("scim-okta");
        const string email = "ana@scim-okta.test";
        var filter = "/api/scim/v2/Users?filter=" + HttpUtility.UrlEncode($"userName eq \"{email}\"") + "&startIndex=1&count=100";

        (await ReadAsync(await scim.GetAsync(filter))).GetProperty("totalResults").GetInt32().Should().Be(0);

        var id = await CreateUserAsync(scim, email, "00u123");
        var found = await ReadAsync(await scim.GetAsync(filter));
        found.GetProperty("totalResults").GetInt32().Should().Be(1);
        var resource = found.GetProperty("Resources")[0];
        resource.GetProperty("id").GetString().Should().Be(id);
        resource.GetProperty("externalId").GetString().Should().Be("00u123");
        resource.GetProperty("name").GetProperty("formatted").GetString().Should().Be("Ana Lyst");
        resource.GetProperty("active").GetBoolean().Should().BeTrue();
        var created = await DbAsync(db => db.OrgUsers.SingleAsync(u => u.Id == Guid.Parse(id)));
        created.IsSsoProvisioned.Should().BeTrue();
        created.PasswordHash.Should().BeEmpty("SCIM users sign in with SSO");

        var duplicate = await scim.PostAsync("/api/scim/v2/Users", Json(new { userName = email, emails = new[] { new { value = email, primary = true } } }));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(duplicate)).GetProperty("scimType").GetString().Should().Be("uniqueness");

        // Give the person a session, then deactivate them: the session ends.
        await GiveSessionAsync(Guid.Parse(id));
        var off = await PatchAsync(scim, $"Users/{id}", new { op = "replace", value = new { active = false } });
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        (await ReadAsync(off)).GetProperty("active").GetBoolean().Should().BeFalse();
        (await DbAsync(db => db.RefreshTokens.AnyAsync(t => t.OrgUserId == Guid.Parse(id) && t.RevokedAt == null))).Should().BeFalse();

        (await PatchAsync(scim, $"Users/{id}", new { op = "replace", value = new { active = true } })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await scim.DeleteAsync($"/api/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await scim.GetAsync($"/api/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync(await scim.GetAsync(filter))).GetProperty("totalResults").GetInt32().Should().Be(0);
        (await DbAsync(db => db.OrgUsers.SingleAsync(u => u.Id == Guid.Parse(id)))).IsActive.Should().BeFalse();

        // Created again later: the same account comes back.
        (await CreateUserAsync(scim, email, "00u123")).Should().Be(id);

        // Audit events are written in the background; give them a moment.
        string[] expected = ["scim.token_created", "scim.user_created", "scim.user_deactivated", "scim.user_deleted", "scim.user_restored"];
        List<string> audit = [];
        for (var i = 0; i < 50 && !expected.All(audit.Contains); i++)
        {
            audit = await DbAsync(db => db.AuditEvents.Where(e => e.OrgId == owner.OrgId).Select(e => e.EventType).ToListAsync());
            if (!expected.All(audit.Contains)) await Task.Delay(100);
        }
        audit.Should().Contain(expected);
    }

    [Fact]
    public async Task Entra_style_patches_are_understood()
    {
        var (_, scim, _) = await EnterpriseWithScimAsync("scim-entra");
        var id = await CreateUserAsync(scim, "ben@scim-entra.test");

        var response = await PatchAsync(scim, $"Users/{id}",
            new { op = "Replace", path = "name.givenName", value = "Benjamin" },
            new { op = "Replace", path = "emails[type eq \"work\"].value", value = "benjamin@scim-entra.test" },
            new { op = "Add", path = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department", value = "Credit" },
            new { op = "Replace", path = "active", value = "False" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var user = await DbAsync(db => db.OrgUsers.SingleAsync(u => u.Id == Guid.Parse(id)));
        user.Name.Should().Be("Benjamin Lyst");
        user.Email.Should().Be("benjamin@scim-entra.test");
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Scim_cannot_deactivate_delete_or_re_email_an_organisation_owner()
    {
        var (owner, scim, _) = await EnterpriseWithScimAsync("scim-owner");
        var ownerId = (await ReadAsync(await scim.GetAsync(
            "/api/scim/v2/Users?filter=" + HttpUtility.UrlEncode($"userName eq \"{owner.Email}\""))))
            .GetProperty("Resources")[0].GetProperty("id").GetString();

        var deactivate = await PatchAsync(scim, $"Users/{ownerId}", new { op = "replace", path = "active", value = false });
        deactivate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(deactivate)).GetProperty("scimType").GetString().Should().Be("mutability");
        (await scim.DeleteAsync($"/api/scim/v2/Users/{ownerId}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PatchAsync(scim, $"Users/{ownerId}", new { op = "replace", path = "emails[type eq \"work\"].value", value = "attacker@evil.test" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Matching the owner (setting externalId, active=true) is fine: that is what an IdP does on first sync.
        (await PatchAsync(scim, $"Users/{ownerId}", new { op = "replace", value = new { externalId = "00uOwner", active = true } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await DbAsync(db => db.OrgUsers.SingleAsync(u => u.Id == Guid.Parse(ownerId!)));
        stored.IsActive.Should().BeTrue();
        stored.Email.Should().Be(owner.Email);
    }

    [Fact]
    public async Task Bad_requests_get_scim_errors_that_say_what_to_fix()
    {
        var (owner, scim, _) = await EnterpriseWithScimAsync("scim-errors");

        var badFilter = await scim.GetAsync("/api/scim/v2/Users?filter=" + HttpUtility.UrlEncode("userName co \"a\""));
        badFilter.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(badFilter)).GetProperty("scimType").GetString().Should().Be("invalidFilter");

        var notJson = await scim.PostAsync("/api/scim/v2/Users", new StringContent("{oops", Encoding.UTF8, "application/scim+json"));
        (await ReadAsync(notJson)).GetProperty("scimType").GetString().Should().Be("invalidSyntax");

        // Allowed email domains from SSO settings apply to SCIM too.
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.OrgSsoConfigs.Add(new Core.Entities.Auth.OrgSsoConfig { OrgId = owner.OrgId, Provider = "saml", AllowedEmailDomains = ["scim-errors.test"] });
            await db.SaveChangesAsync();
        }
        var outside = await scim.PostAsync("/api/scim/v2/Users", Json(new { userName = "x@gmail.com" }));
        outside.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(outside)).GetProperty("detail").GetString().Should().Contain("gmail.com");
    }

    // ── Groups and roles ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scim_groups_set_workspace_roles_through_the_sso_mappings()
    {
        var (owner, scim, _) = await EnterpriseWithScimAsync("scim-groups");
        var ws = await CreateWorkspaceAsync(owner.Client, "Lending", "scim-groups-ws");
        var put = await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "saml", isActive = true, idpEntityId = SamlTestIdp.IdpEntityId, idpSsoUrl = "https://idp.acme.test/sso",
            idpCertificate = _idp.CertificatePem,
            groupRoleMappings = new[] { new { group = "Credit", workspaceId = ws, role = "Operator" }, new { group = "Credit-Leads", workspaceId = ws, role = "Admin" } },
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        var ana = await CreateUserAsync(scim, "ana@scim-groups.test");
        var ben = await CreateUserAsync(scim, "ben@scim-groups.test");
        Task<WorkspaceMember?> Member(string id) =>
            DbAsync(db => db.WorkspaceMembers.AsNoTracking().SingleOrDefaultAsync(m => m.WorkspaceId == ws && m.OrgUserId == Guid.Parse(id)));
        (await Member(ana)).Should().BeNull("no group yet and no default workspace");

        var create = await scim.PostAsync("/api/scim/v2/Groups", Json(new { displayName = "Credit", externalId = "g1", members = new[] { new { value = ana } } }));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var groupId = (await ReadAsync(create)).GetProperty("id").GetString();
        (await Member(ana)).Should().Match<WorkspaceMember>(m => m.IsActive && m.ManagedBySso && m.Role == WorkspaceRole.Operator);

        (await PatchAsync(scim, $"Groups/{groupId}", new { op = "add", path = "members", value = new[] { new { value = ben } } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Member(ben))!.Role.Should().Be(WorkspaceRole.Operator);

        var listed = await ReadAsync(await scim.GetAsync("/api/scim/v2/Groups?filter=" + HttpUtility.UrlEncode("displayName eq \"Credit\"")));
        listed.GetProperty("Resources")[0].GetProperty("members").GetArrayLength().Should().Be(2);
        var slim = await ReadAsync(await scim.GetAsync($"/api/scim/v2/Groups/{groupId}?excludedAttributes=members"));
        slim.TryGetProperty("members", out _).Should().BeFalse();

        // Entra removes with members[value eq "…"]; the access SCIM gave goes away.
        (await PatchAsync(scim, $"Groups/{groupId}", new { op = "Remove", path = $"members[value eq \"{ana}\"]" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Member(ana))!.IsActive.Should().BeFalse();

        // Renaming the group to a name mapped to Admin changes the remaining member's role.
        (await PatchAsync(scim, $"Groups/{groupId}", new { op = "replace", value = new { displayName = "Credit-Leads" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Member(ben))!.Role.Should().Be(WorkspaceRole.Admin);

        (await scim.DeleteAsync($"/api/scim/v2/Groups/{groupId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Member(ben))!.Should().Match<WorkspaceMember>(m => !m.IsActive || m.Role == WorkspaceRole.Admin,
            "the last Admin of a workspace is never removed by sync; otherwise access goes");

        var unknown = await scim.PostAsync("/api/scim/v2/Groups", Json(new { displayName = "Ops", members = new[] { new { value = Guid.NewGuid() } } }));
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task With_scim_in_use_sso_sign_in_takes_roles_from_scim_groups()
    {
        const string slug = "scim-sso-roles";
        var (owner, scim, _) = await EnterpriseWithScimAsync(slug);
        var ws = await CreateWorkspaceAsync(owner.Client, "Lending", $"{slug}-ws");
        (await owner.Client.PutAsJsonAsync($"/api/v1/organisations/{owner.OrgId}/sso", new
        {
            provider = "saml", isActive = true, idpEntityId = SamlTestIdp.IdpEntityId, idpSsoUrl = "https://idp.acme.test/sso",
            idpCertificate = _idp.CertificatePem,
            groupRoleMappings = new[] { new { group = "Credit", workspaceId = ws, role = "Operator" } },
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        var ana = await CreateUserAsync(scim, $"ana@{slug}.test");
        (await scim.PostAsync("/api/scim/v2/Groups", Json(new { displayName = "Credit", members = new[] { new { value = ana } } })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // The assertion carries no groups (common when SCIM is set up); the SCIM group still decides.
        var browser = Fixture.NewClientWithoutRedirects();
        var start = await browser.GetAsync($"/api/v1/saml/{slug}/login");
        var query = HttpUtility.ParseQueryString(start.Headers.Location!.Query);
        using var inflate = new System.IO.Compression.DeflateStream(new MemoryStream(Convert.FromBase64String(query["SAMLRequest"]!)),
            System.IO.Compression.CompressionMode.Decompress);
        var requestId = System.Text.RegularExpressions.Regex.Match(new StreamReader(inflate).ReadToEnd(), "ID=\"([^\"]+)\"").Groups[1].Value;
        var response = _idp.Build(new()
        {
            Email = $"ana@{slug}.test", Groups = [], InResponseTo = requestId,
            Audience = $"{App}/saml/{slug}", Recipient = $"{App}/api/v1/saml/{slug}/acs",
        });
        var acs = await browser.PostAsync($"/api/v1/saml/{slug}/acs", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = response, ["RelayState"] = query["RelayState"]!,
        }));
        acs.Headers.Location!.ToString().Should().NotContain("sso_error");

        var member = await DbAsync(db => db.WorkspaceMembers.AsNoTracking().SingleAsync(m => m.WorkspaceId == ws && m.OrgUserId == Guid.Parse(ana)));
        member.IsActive.Should().BeTrue();
        member.Role.Should().Be(WorkspaceRole.Operator);
    }

    private async Task GiveSessionAsync(Guid userId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        db.RefreshTokens.Add(new Core.Entities.Auth.RefreshToken
        {
            OrgUserId = userId, TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        await db.SaveChangesAsync();
    }
}
