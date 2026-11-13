using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Library;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Flowamaz.Tests.Integration.Common.FakeLibrarySource;

namespace Flowamaz.Tests.Integration.Phase8;

/// <summary>Library Forms and Packs (FUNCTIONAL.md §23.2): gate forms end to end, workspace forms, pack installs, sync.</summary>
[Collection("api")]
public sealed class LibraryFormsAndPacksTests : ApiTestBase
{
    public LibraryFormsAndPacksTests(IntegrationApiFixture fixture) : base(fixture) { }

    private static string U(string slug) => $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

    private const string GatedYaml = """
        workflow: { id: gated-form, version: v1, name: Gated form }
        nodes:
          - { id: start, type: Trigger }
          - id: approval
            type: HumanGate
            config:
              form:
                fields:
                  - { key: amount, label: Approved amount, type: number, required: true, min: 0 }
                  - { key: cost_centre, label: Cost centre, type: text, required: true }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: approval }
          - { id: e2, from: approval, to: done }
        """;

    [Fact]
    public async Task A_gate_with_a_form_needs_its_answers_and_passes_them_on()
    {
        var owner = await RegisterOwnerAsync(U("gate-form"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Finance", U("gate-form-ws"));
        var create = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows",
            new { name = "Gated form", slug = "gated-form", yamlContent = GatedYaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();
        await owner.Client.PostAsync($"/api/v1/workspaces/{ws}/workflows/{workflowId}/publish", null);
        var trigger = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/instances", new { workflowDefinitionId = workflowId });
        var instanceId = (await DataAsync(trigger)).GetProperty("instanceId").GetGuid();

        const string formJson = """[{"key":"amount","label":"Approved amount","type":"number","required":true,"min":0},{"key":"cost_centre","label":"Cost centre","type":"text","required":true}]""";
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.WorkflowNodeStates.Add(new WorkflowNodeState { WorkspaceId = ws, InstanceId = instanceId, NodeId = "approval", NodeType = "HumanGate", Status = NodeStatus.Pending });
            db.GateDecisions.Add(new GateDecision
            {
                WorkspaceId = ws, InstanceId = instanceId, NodeId = "approval", Decision = GateDecisionStatus.Pending,
                DeliveryChannel = GateDeliveryChannel.Portal, DeliveryStatus = GateDeliveryStatus.Sent, FormJson = formJson,
            });
            await db.SaveChangesAsync();
        }

        // The pending gate shows the approver its fields.
        var list = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/gates"));
        var gate = list.EnumerateArray().Single(g => g.GetProperty("instanceId").GetGuid() == instanceId);
        gate.GetProperty("form").EnumerateArray().Select(f => f.GetProperty("key").GetString()).Should().Equal("amount", "cost_centre");

        // Approving without the answers is refused, saying what is missing.
        var url = $"/api/v1/workspaces/{ws}/gates/{instanceId}/approval/decide";
        var missing = await owner.Client.PostAsJsonAsync(url, new { decision = "approved", values = new { amount = -5 } });
        missing.StatusCode.Should().Be((HttpStatusCode)422);
        var message = await MessageAsync(missing);
        message.Should().Contain("Approved amount must be at least 0.").And.Contain("Cost centre is required.").And.Contain("approve again");

        // With the answers it goes through, and they are kept with the decision.
        var ok = await owner.Client.PostAsJsonAsync(url, new { decision = "approved", values = new { amount = "1200", cost_centre = "CC-7" } });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var decided = await DataAsync(ok);
        decided.GetProperty("formValues").GetProperty("amount").GetDouble().Should().Be(1200);
        decided.GetProperty("formValues").GetProperty("cost_centre").GetString().Should().Be("CC-7");
    }

    [Fact]
    public async Task A_rejection_needs_no_form_answers()
    {
        var owner = await RegisterOwnerAsync(U("gate-form-no"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Finance", U("gate-form-no-ws"));
        var instanceId = Guid.NewGuid();
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.GateDecisions.Add(new GateDecision
            {
                WorkspaceId = ws, InstanceId = instanceId, NodeId = "approval", Decision = GateDecisionStatus.Pending,
                DeliveryChannel = GateDeliveryChannel.Portal, DeliveryStatus = GateDeliveryStatus.Sent,
                FormJson = """[{"key":"amount","label":"Amount","type":"number","required":true}]""",
            });
            await db.SaveChangesAsync();
        }

        var reject = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/gates/{instanceId}/approval/decide", new { decision = "rejected", note = "no budget" });

        // Whatever the instance does next, the form never blocks a rejection.
        reject.StatusCode.Should().NotBe((HttpStatusCode)422, await reject.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Workspace_forms_are_private_and_library_forms_are_read_only()
    {
        var a = await RegisterOwnerAsync(U("forms-a"));
        var wsA = await CreateWorkspaceAsync(a.Client, "A", U("forms-a-ws"));
        var b = await RegisterOwnerAsync(U("forms-b"));
        var wsB = await CreateWorkspaceAsync(b.Client, "B", U("forms-b-ws"));

        var library = await DataAsync(await a.Client.GetAsync($"/api/v1/workspaces/{wsA}/library/forms"));
        library.EnumerateArray().Should().Contain(f => f.GetProperty("slug").GetString() == "purchase-details" && f.GetProperty("isOfficial").GetBoolean());

        var save = await a.Client.PostAsJsonAsync($"/api/v1/workspaces/{wsA}/library/forms", new
        {
            name = "Vendor check", category = "Finance",
            fields = new object[] { new { key = "vat_number", label = "VAT number", type = "text", required = true } },
        });
        save.StatusCode.Should().Be(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        var saved = await DataAsync(save);
        saved.GetProperty("slug").GetString().Should().Be("vendor-check");
        saved.GetProperty("isWorkspaceForm").GetBoolean().Should().BeTrue();

        (await a.Client.PostAsJsonAsync($"/api/v1/workspaces/{wsA}/library/forms", new
        {
            name = "Vendor check", fields = new object[] { new { key = "x", label = "X", type = "text" } },
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var badField = await a.Client.PostAsJsonAsync($"/api/v1/workspaces/{wsA}/library/forms", new
        {
            name = "Broken", fields = new object[] { new { key = "kind", label = "Kind", type = "select" } },
        });
        badField.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessageAsync(badField)).Should().Contain("has no options");

        // Workspace B never sees A's form, by list or by id.
        var listB = await DataAsync(await b.Client.GetAsync($"/api/v1/workspaces/{wsB}/library/forms"));
        listB.EnumerateArray().Should().NotContain(f => f.GetProperty("slug").GetString() == "vendor-check");
        (await b.Client.GetAsync($"/api/v1/workspaces/{wsB}/library/forms/{saved.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Client.DeleteAsync($"/api/v1/workspaces/{wsB}/library/forms/{saved.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var official = library.EnumerateArray().First(f => f.GetProperty("slug").GetString() == "purchase-details").GetProperty("id").GetGuid();
        (await a.Client.DeleteAsync($"/api/v1/workspaces/{wsA}/library/forms/{official}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await a.Client.DeleteAsync($"/api/v1/workspaces/{wsA}/library/forms/{saved.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Installing_a_pack_installs_each_item_once()
    {
        var owner = await RegisterOwnerAsync(U("pack"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Finance", U("pack-ws"));

        var packs = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/packs"));
        var finance = packs.EnumerateArray().Single(p => p.GetProperty("slug").GetString() == "finance-approvals");
        finance.GetProperty("items").EnumerateArray().Should().OnlyContain(i => !i.GetProperty("installed").GetBoolean() || i.GetProperty("kind").GetString() == "form");

        var first = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/packs/install", new { id = "finance-approvals" });
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var result = await DataAsync(first);
        var items = result.GetProperty("items").EnumerateArray().ToList();
        items.Where(i => i.GetProperty("kind").GetString() != "form").Should()
            .OnlyContain(i => i.GetProperty("status").GetString() == "installed", string.Join("; ", items.Select(i => i.GetProperty("message").GetString())));
        result.GetProperty("installed").GetInt32().Should().Be(5, "2 connectors + 3 templates");
        items.Where(i => i.GetProperty("kind").GetString() == "template").Should().OnlyContain(i => i.GetProperty("workflowId").GetGuid() != Guid.Empty);

        var again = await DataAsync(await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/packs/install", new { id = "finance-approvals" }));
        again.GetProperty("installed").GetInt32().Should().Be(0);
        again.GetProperty("alreadyInstalled").GetInt32().Should().Be(7);

        var workflows = await DbAsync(db => db.WorkflowDefinitions.CountAsync(w => w.WorkspaceId == ws));
        workflows.Should().Be(3);

        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/library/packs/install", new { id = "no-such-pack" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Forms_and_packs_sync_from_the_templates_repository()
    {
        var form = U("intake");
        var pack = U("acme-ops");
        Fixture.Library.Set("templates", Guid.NewGuid().ToString("N"),
            Folder($"forms/acme/{form}", ("form.yaml", "name: Intake\nversion: 1.0.0\nfields:\n  - { key: reason, label: Reason, type: textarea, required: true }\n")),
            Folder($"packs/acme/{pack}", ("pack.yaml", $"name: Acme ops\nversion: 1.0.0\ntemplates: [purchase-approval]\nforms: [acme-{form}]\n")),
            Folder("forms/acme/broken", ("form.yaml", "version: 1.0.0\nfields: []\n")));

        var report = (await SyncAsync()).Single(r => r.Source == "templates");
        report.Outcome.Should().Be("synced");
        report.Skipped.Should().Contain(s => s.Contains("forms/acme/broken"));

        var owner = await RegisterOwnerAsync(U("sync-forms"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Ops", U("sync-forms-ws"));
        var synced = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/forms/acme-{form}"));
        synced.GetProperty("fields")[0].GetProperty("key").GetString().Should().Be("reason");
        var syncedPack = await DataAsync(await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/library/packs/acme-{pack}"));
        syncedPack.GetProperty("items").EnumerateArray().Should().OnlyContain(i => i.GetProperty("available").GetBoolean());

        // Taking the folders out deprecates them; the seeded official forms and packs stay.
        Fixture.Library.Set("templates", Guid.NewGuid().ToString("N"));
        await SyncAsync();
        var after = await DbAsync(db => db.FormDefinitions.SingleAsync(f => f.Slug == $"acme-{form}"));
        after.DeprecatedAt.Should().NotBeNull();
        (await DbAsync(db => db.PackDefinitions.SingleAsync(p => p.Slug == $"acme-{pack}"))).DeprecatedAt.Should().NotBeNull();
        (await DbAsync(db => db.FormDefinitions.SingleAsync(f => f.Slug == "purchase-details"))).DeprecatedAt.Should().BeNull();
        (await DbAsync(db => db.PackDefinitions.SingleAsync(p => p.Slug == "finance-approvals"))).DeprecatedAt.Should().BeNull();
    }

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
}
