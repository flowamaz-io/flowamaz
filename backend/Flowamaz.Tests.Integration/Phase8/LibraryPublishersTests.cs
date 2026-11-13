using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase8;

/// <summary>Library publishers (FUNCTIONAL.md §23.9): profiles, the portal, and staff review of app-published templates.</summary>
[Collection("api")]
public sealed class LibraryPublishersTests : ApiTestBase
{
    public LibraryPublishersTests(IntegrationApiFixture fixture) : base(fixture) { }

    private static string U(string slug) => $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

    private static string TemplateManifest(string title) => $$"""
        kind: template
        name: {{title}}
        version: 0.1.0
        description: Onboard a vendor
        workflow:
          apiVersion: flowamaz/v1
          kind: Workflow
          metadata: { id: vendor-onboarding, name: Vendor onboarding, version: 1.0.0 }
          spec:
            trigger: { type: manual }
            nodes:
              - { id: start, type: trigger, label: Start }
              - { id: done, type: end, label: Done }
            edges:
              - { id: e1, from: start, to: done }
        """;

    [Fact]
    public async Task A_template_published_from_the_app_goes_live_when_staff_approve_it()
    {
        var owner = await RegisterOwnerAsync(U("pub-review"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", U("pub-review-ws"));
        var title = $"Vendor intake {Guid.NewGuid().ToString("N")[..4]}";
        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/templates/publish", new { manifest = TemplateManifest(title) }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var templateId = await DbAsync(db => db.WorkflowTemplates.Where(t => t.Name == title).Select(t => t.Id).SingleAsync());

        // Pending: in the portal, not in the Library, not on the profile.
        var portal = await DataAsync(await owner.Client.GetAsync("/api/v1/publisher/portal"));
        portal.GetProperty("publisherId").GetString().Should().Be(owner.OrgSlug);
        var pending = portal.GetProperty("submissions").EnumerateArray().Single(s => s.GetProperty("name").GetString() == title);
        pending.GetProperty("status").GetString().Should().Be("pending");
        pending.GetProperty("statusText").GetString().Should().Contain("Waiting for review");
        (await LibraryTemplateNamesAsync(owner.Client, ws)).Should().NotContain(title);

        var staff = await RegisterOwnerAsync(U("pub-staff"));
        (await staff.Client.GetAsync("/api/v1/platform/library/reviews")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.Client.PostAsJsonAsync($"/api/v1/platform/library/reviews/templates/{templateId}/approve", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "a publisher can't approve their own template");

        var config = Fixture.Services.GetRequiredService<IConfiguration>();
        config["Platform:StaffOrgIds"] = staff.OrgId.ToString();
        try
        {
            var queue = await DataAsync(await staff.Client.GetAsync("/api/v1/platform/library/reviews"));
            var item = queue.GetProperty("templates").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == templateId);
            item.GetProperty("publisherId").GetString().Should().Be(owner.OrgSlug);
            item.GetProperty("yamlContent").GetString().Should().Contain("apiVersion: flowamaz/v1");

            var approve = await staff.Client.PostAsJsonAsync($"/api/v1/platform/library/reviews/templates/{templateId}/approve", new { notes = "Clear and well tested." });
            approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
            (await staff.Client.PostAsJsonAsync($"/api/v1/platform/library/reviews/templates/{templateId}/approve", new { }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, "it's no longer waiting");
        }
        finally
        {
            config["Platform:StaffOrgIds"] = null;
        }

        (await LibraryTemplateNamesAsync(owner.Client, ws)).Should().Contain(title);
        var profile = await DataAsync(await staff.Client.GetAsync($"/api/v1/library/publishers/{owner.OrgSlug}"));
        profile.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("name").GetString() == title);
        var notes = await DbAsync(db => db.Notifications.Where(n => n.OrgId == owner.OrgId && n.Type == "library_template_approved").CountAsync());
        notes.Should().Be(1);
    }

    [Fact]
    public async Task Rejecting_needs_a_note_and_the_publisher_sees_it()
    {
        var owner = await RegisterOwnerAsync(U("pub-reject"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", U("pub-reject-ws"));
        var title = $"Vendor reject {Guid.NewGuid().ToString("N")[..4]}";
        await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/templates/publish", new { manifest = TemplateManifest(title) });
        var templateId = await DbAsync(db => db.WorkflowTemplates.Where(t => t.Name == title).Select(t => t.Id).SingleAsync());
        var staff = await RegisterOwnerAsync(U("pub-staff2"));
        var config = Fixture.Services.GetRequiredService<IConfiguration>();
        config["Platform:StaffOrgIds"] = staff.OrgId.ToString();
        try
        {
            var bare = await staff.Client.PostAsJsonAsync($"/api/v1/platform/library/reviews/templates/{templateId}/reject", new { notes = "" });
            bare.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await MessageAsync(bare)).Should().Contain("at least 10 characters");
            (await staff.Client.PostAsJsonAsync($"/api/v1/platform/library/reviews/templates/{templateId}/reject",
                new { notes = "The workflow has no approval step; add one before the end." })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            config["Platform:StaffOrgIds"] = null;
        }

        var portal = await DataAsync(await owner.Client.GetAsync("/api/v1/publisher/portal"));
        var s = portal.GetProperty("submissions").EnumerateArray().Single(x => x.GetProperty("name").GetString() == title);
        s.GetProperty("status").GetString().Should().Be("rejected");
        s.GetProperty("reviewerNotes").GetString().Should().Be("The workflow has no approval step; add one before the end.");
    }

    [Fact]
    public async Task Only_the_owner_edits_the_profile_and_anyone_signed_in_can_read_it()
    {
        var owner = await RegisterOwnerAsync(U("pub-profile"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", U("pub-profile-ws"));
        var memberId = await AddPersonAsync(owner.OrgId, $"designer-{Guid.NewGuid():N}@acme.test", ws);
        var member = await SignedInAsync(memberId);

        (await member.GetAsync("/api/v1/publisher/portal")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync("/api/v1/publisher/profile", new { displayName = "Hijack" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var bad = await owner.Client.PutAsJsonAsync("/api/v1/publisher/profile", new { displayName = "Acme", websiteUrl = "http://acme.example" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessageAsync(bad)).Should().Contain("https://");

        var ok = await owner.Client.PutAsJsonAsync("/api/v1/publisher/profile",
            new { displayName = "Acme Labs", bio = "Finance connectors.", websiteUrl = "https://acme.example", githubLogin = "@acme-labs" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());

        var stranger = await RegisterOwnerAsync(U("pub-stranger"));
        var profile = await DataAsync(await stranger.Client.GetAsync($"/api/v1/library/publishers/{owner.OrgSlug}"));
        profile.GetProperty("displayName").GetString().Should().Be("Acme Labs");
        profile.GetProperty("githubLogin").GetString().Should().Be("acme-labs");
        profile.GetProperty("isVerified").GetBoolean().Should().BeFalse();

        var official = await DataAsync(await stranger.Client.GetAsync("/api/v1/library/publishers/flowamaz-io"));
        official.GetProperty("isVerified").GetBoolean().Should().BeTrue();
        official.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).Distinct()
            .Should().BeEquivalentTo(["connector", "template", "form", "pack"]);
        (await stranger.Client.GetAsync("/api/v1/library/publishers/no-such-publisher-x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Fixture.NewClient().GetAsync("/api/v1/library/publishers/flowamaz-io")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<List<string>> LibraryTemplateNamesAsync(HttpClient client, Guid ws)
    {
        var list = await DataAsync(await client.GetAsync($"/api/v1/workspaces/{ws}/library/templates"));
        var items = list.ValueKind == JsonValueKind.Array ? list : list.GetProperty("data");
        return items.EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
    }

    private async Task<T> DbAsync<T>(Func<FlowAmazDbContext, Task<T>> query)
    {
        using var scope = Fixture.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>());
    }

    private async Task<Guid> AddPersonAsync(Guid orgId, string email, Guid workspaceId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var user = new OrgUser { OrgId = orgId, Email = email, Name = email.Split('@')[0], PasswordHash = string.Empty, IsActive = true };
        db.OrgUsers.Add(user);
        db.WorkspaceMembers.Add(new Flowamaz.Core.Entities.Workspaces.WorkspaceMember
        {
            WorkspaceId = workspaceId, OrgUserId = user.Id, Role = Flowamaz.Core.Enums.WorkspaceRole.Designer, IsActive = true,
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<HttpClient> SignedInAsync(Guid userId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        var user = await db.OrgUsers.AsNoTracking().SingleAsync(u => u.Id == userId);
        var slug = await db.Organisations.Where(o => o.Id == user.OrgId).Select(o => o.Slug).SingleAsync();
        var memberships = await scope.ServiceProvider.GetRequiredService<IWorkspaceMemberRepository>().GetActiveMembershipsForUserAsync(userId);
        var token = scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateAccessToken(user, slug, memberships);
        var client = Fixture.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
