using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Flowamaz.Tests.Integration.Common.FakeLibrarySource;

namespace Flowamaz.Tests.Integration.Phase8;

/// <summary>
/// Library GitHub sync (FUNCTIONAL.md §23.7), connector versioning (§23.8) and the workspace Library API the
/// <c>fmz library</c> commands use (§23.5), against a fake repository.
/// </summary>
[Collection("api")]
public sealed class LibrarySyncAndVersioningTests : ApiTestBase
{
    public LibrarySyncAndVersioningTests(IntegrationApiFixture fixture) : base(fixture) { }

    private static string U(string slug) => $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
    private static string Sha() => Guid.NewGuid().ToString("N");

    private static string Manifest(string id, string version, string extra = "") => $"""
        id: {id}
        publisher: acme
        version: {version}
        display_name: Invoice Sync
        category: finance
        operations:
          - id: create-invoice
            method: POST
            url: https://api.acme.test/invoices
        {extra}
        """;

    private async Task<IReadOnlyList<Core.Models.LibrarySyncReport>> SyncAsync()
    {
        using var scope = Fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILibrarySyncService>().SyncAsync();
    }

    private async Task<T> DbAsync<T>(Func<FlowAmazDbContext, Task<T>> query)
    {
        using var scope = Fixture.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>());
    }

    [Fact]
    public async Task A_connector_moves_through_minor_updates_a_new_major_and_deprecation()
    {
        var id = U("invoice-sync");
        var folder = $"connectors/acme/{id}";
        var owner = await RegisterOwnerAsync(U("lib-versions"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Finance", U("lib-versions-ws"));

        // 1. The connector lands in the repository and becomes installable.
        Fixture.Library.Set("connectors", Sha(), Folder(folder, ("connector.yaml", Manifest(id, "1.2.0"))));
        var first = (await SyncAsync()).Single(r => r.Source == "connectors");
        first.Outcome.Should().Be("synced");
        first.Added.Should().BeGreaterThanOrEqualTo(1);

        var install = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/connectors/install", new { id });
        install.StatusCode.Should().Be(HttpStatusCode.OK);
        var installed = await DataAsync(install);
        installed.GetProperty("installedPin").GetString().Should().Be($"acme/{id}@1");
        installed.GetProperty("installedVersion").GetString().Should().Be("1.2.0");

        // 2. A minor release is applied in place, with a changelog notice to the workspace admins.
        Fixture.Library.Set("connectors", Sha(), Folder(folder,
            ("connector.yaml", Manifest(id, "1.3.0")),
            ("CHANGELOG.md", "## 1.3.0 (2026-11-02)\n- Adds currency.\n\n## 1.2.0\nFirst release.")));
        (await SyncAsync()).Single(r => r.Source == "connectors").Updated.Should().BeGreaterThanOrEqualTo(1);

        var running = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/connectors/{id}"));
        running.GetProperty("connector").GetProperty("installedVersion").GetString().Should().Be("1.3.0");
        var notices = await DbAsync(db => db.Notifications.Where(n => n.WorkspaceId == ws && n.Type == "connector_updated").ToListAsync());
        notices.Should().ContainSingle().Which.Message.Should().Contain("from 1.2.0 to 1.3.0").And.Contain("Adds currency");
        (await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/connectors/outdated"))).GetArrayLength().Should().Be(0);

        // 3. A new major is a new row: the workspace stays pinned to @1 until an admin upgrades.
        Fixture.Library.Set("connectors", Sha(), Folder(folder,
            ("connector.yaml", Manifest(id, "2.0.0")),
            ("CHANGELOG.md", "## 2.0.0 (2026-11-10)\n- Renames amount to total (breaking).\n\n## 1.3.0\n- Adds currency.")));
        await SyncAsync();

        var outdated = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/connectors/outdated"));
        outdated.GetArrayLength().Should().Be(1);
        outdated[0].GetProperty("installedPin").GetString().Should().Be($"acme/{id}@1");
        outdated[0].GetProperty("latestVersion").GetString().Should().Be("2.0.0");
        outdated[0].GetProperty("changes")[0].GetProperty("notes").GetString().Should().Contain("breaking");
        (await DbAsync(db => db.Notifications.CountAsync(n => n.WorkspaceId == ws && n.Type == "connector_major_available"))).Should().Be(1);
        var pinned = await DbAsync(db => db.ConnectorDefinitions.SingleAsync(c => c.ConnectorId == id && c.MajorVersion == 1));
        pinned.DeprecatedAt.Should().BeNull("an older major is superseded, not removed");
        pinned.IsLatestMajor.Should().BeFalse();

        // Upgrading is an admin decision: a Designer can't, the owner (workspace Admin) can.
        var upgrade = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/connectors/update", new { id });
        upgrade.StatusCode.Should().Be(HttpStatusCode.OK);
        var upgraded = await DataAsync(upgrade);
        upgraded.GetProperty("fromVersion").GetString().Should().Be("1.3.0");
        upgraded.GetProperty("pin").GetString().Should().Be($"acme/{id}@2");
        var again = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/connectors/update", new { id });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MessageAsync(again)).Should().Contain("already on the latest major").And.Contain("applied automatically");

        // 4. Removed from the repository: a 90-day notice; it keeps running, and leaves the install list after.
        Fixture.Library.Set("connectors", Sha());
        await SyncAsync();
        var row = await DbAsync(db => db.ConnectorDefinitions.SingleAsync(c => c.ConnectorId == id && c.MajorVersion == 2));
        row.DeprecatedAt.Should().NotBeNull();
        row.DeprecationNote.Should().Be("Removed from the Library repository.");
        (await DbAsync(db => db.Notifications.CountAsync(n => n.WorkspaceId == ws && n.Type == "connector_deprecated"))).Should().Be(1);

        var otherWs = await CreateWorkspaceAsync(owner.Client, "Ops", U("lib-versions-ops"));
        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{otherWs}/library/connectors/install", new { id }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "within the notice it can still be installed");
        await DbAsync(db => db.ConnectorDefinitions.Where(c => c.ConnectorId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeprecatedAt, DateTime.UtcNow.AddDays(-91))));
        var thirdWs = await CreateWorkspaceAsync(owner.Client, "Legal", U("lib-versions-legal"));
        var late = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{thirdWs}/library/connectors/install", new { id });
        late.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await MessageAsync(late)).Should().Contain("left the Library");
        var list = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{thirdWs}/library/connectors?q={id}"));
        list.GetArrayLength().Should().Be(0);
        var stillMine = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/connectors?q={id}"));
        stillMine.GetArrayLength().Should().Be(1, "a workspace that runs it still sees it");
    }

    [Fact]
    public async Task Bad_folders_are_skipped_and_a_failing_repository_keeps_the_last_known_state()
    {
        var good = U("good-conn");
        Fixture.Library.Set("connectors", Sha(),
            Folder($"connectors/acme/{good}", ("connector.yaml", Manifest(good, "1.0.0"))),
            Folder("connectors/acme/spoofed", ("connector.yaml", Manifest("spoofed", "1.0.0").Replace("publisher: acme", "publisher: flowamaz-io"))),
            Folder("connectors/acme/fake-official", ("connector.yaml", Manifest("fake-official", "1.0.0", "tier: official"))),
            Folder("connectors/acme/no-version", ("connector.yaml", "id: no-version\n")));

        var report = (await SyncAsync()).Single(r => r.Source == "connectors");

        report.Skipped.Should().HaveCount(3);
        report.Skipped.Should().Contain(s => s.Contains("doesn't match its folder"))
            .And.Contain(s => s.Contains("only flowamaz-io publishes official"))
            .And.Contain(s => s.Contains("version is missing"));
        (await DbAsync(db => db.ConnectorDefinitions.AnyAsync(c => c.ConnectorId == good))).Should().BeTrue();

        // Three failed fetches in a row: nothing changes, and the run of failures is recorded for the alert.
        Fixture.Library.Fail("connectors", "GitHub answered 502");
        for (var i = 0; i < 3; i++)
            (await SyncAsync()).Single(r => r.Source == "connectors").Outcome.Should().Be("failed");
        var state = await DbAsync(db => db.LibrarySyncStates.SingleAsync(s => s.Source == "connectors"));
        state.ConsecutiveFailures.Should().BeGreaterThanOrEqualTo(3);
        state.AlertedAt.Should().NotBeNull();
        state.LastError.Should().Contain("502");
        var stillThere = await DbAsync(db => db.ConnectorDefinitions.SingleAsync(c => c.ConnectorId == good));
        stillThere.DeprecatedAt.Should().BeNull("a failed sync serves the last known state");
    }

    [Fact]
    public async Task A_workspace_private_connector_is_invisible_to_other_workspaces()
    {
        var owner = await RegisterOwnerAsync(U("lib-isolation"));
        var a = await CreateWorkspaceAsync(owner.Client, "A", U("lib-iso-a"));
        var b = await CreateWorkspaceAsync(owner.Client, "B", U("lib-iso-b"));
        var id = U("private-conn");
        await DbAsync(async db =>
        {
            db.ConnectorDefinitions.Add(new ConnectorDefinition
            {
                ConnectorId = id, PublisherId = "acme", DisplayName = "Private", Version = "1.0.0", Category = "generic",
                ManifestJson = "{}", Tier = Core.Enums.ConnectorTier.Community, WorkspaceId = a, IsEnabled = true,
            });
            return await db.SaveChangesAsync();
        });

        (await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{a}/library/connectors?q={id}"))).GetArrayLength().Should().Be(1);
        (await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{b}/library/connectors?q={id}"))).GetArrayLength().Should().Be(0);
        (await owner.Client.GetAsync($"/api/v1/workspaces/{b}/library/connectors/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.Client.GetAsync($"/api/v1/workspaces/{b}/connectors/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Templates_sync_install_by_slug_and_say_they_dont_update_in_place()
    {
        var owner = await RegisterOwnerAsync(U("lib-templates"));
        var ws = await CreateWorkspaceAsync(owner.Client, "HR", U("lib-templates-ws"));
        var name = U("leave-request");
        const string workflow = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              name: Leave request
            spec:
              trigger: { type: manual }
              nodes:
                - { id: start, type: trigger, label: Start }
                - { id: done, type: end, label: Done }
              edges:
                - { id: e1, from: start, to: done }
            """;
        Fixture.Library.Set("templates", Sha(), Folder($"templates/acme/{name}",
            ("template.yaml", "name: Leave request\nversion: 1.0.0\ncategory: HR\ndescription: Ask for leave\n"),
            ("workflow.yaml", workflow)));
        (await SyncAsync()).Single(r => r.Source == "templates").Added.Should().Be(1);

        var slug = $"acme-{name}";
        var shown = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/templates/{slug}"));
        shown.GetProperty("publisher").GetString().Should().Be("acme");
        var install = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/templates/install", new { id = slug, name = "Leave (HR)" });
        install.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(install)).GetProperty("workflowId").GetGuid().Should().NotBeEmpty();

        var outdated = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/templates/outdated");
        outdated.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessageAsync(outdated)).Should().Contain("don't update in place");
    }

    [Fact]
    public async Task Fmz_template_publish_creates_a_pending_community_template_from_its_manifest()
    {
        var owner = await RegisterOwnerAsync(U("lib-publish"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", U("lib-publish-ws"));
        var title = $"Vendor onboarding {Guid.NewGuid().ToString("N")[..4]}";
        var manifest = $$"""
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

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/templates/publish", new { manifest });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await DataAsync(response);
        result.GetProperty("status").GetString().Should().Be("pending");
        var row = await DbAsync(db => db.WorkflowTemplates.SingleAsync(t => t.Name == title));
        row.ReviewStatus.Should().Be("pending");
        row.OrgId.Should().Be(owner.OrgId);
        row.YamlContent.Should().Contain("apiVersion: flowamaz/v1");

        var empty = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/templates/publish", new { manifest = "kind: template\nname: X\n" });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessageAsync(empty)).Should().Contain("has no workflow");
    }

    [Fact]
    public async Task The_push_webhook_checks_the_signature_and_the_branch()
    {
        var client = Fixture.NewClient();
        static string Sign(string body) =>
            "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(IntegrationApiFixture.LibraryWebhookSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        async Task<HttpResponseMessage> PostAsync(string body, string signature, string gitHubEvent = "push")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/library/github") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Hub-Signature-256", signature);
            request.Headers.Add("X-GitHub-Event", gitHubEvent);
            return await client.SendAsync(request);
        }

        const string push = """{"ref":"refs/heads/main"}""";
        (await PostAsync(push, "sha256=00")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(push, Sign(push))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var other = await PostAsync("""{"ref":"refs/heads/feature"}""", Sign("""{"ref":"refs/heads/feature"}"""));
        other.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await other.Content.ReadAsStringAsync()).Should().Contain("ignored");
        (await PostAsync("{}", Sign("{}"), "ping")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
