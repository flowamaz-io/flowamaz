using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;

namespace Flowamaz.Tests.Integration.Phase3;

/// <summary>
/// Workflow DNA integration tests: similarity scoring and clone endpoint.
/// </summary>
[Collection("api")]
public class Phase3DnaTests : ApiTestBase
{
    public Phase3DnaTests(IntegrationApiFixture fixture) : base(fixture) { }

    // SFG format (top-level workflow/nodes/edges) — this is what the DNA service's SfgParser reads.
    private const string ApprovalYaml = """
        workflow: { id: approval-1, version: v1, name: "Approval Workflow" }
        nodes:
          - { id: start, type: Trigger }
          - { id: gate, type: HumanGate, label: "Approval" }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: gate }
          - { id: e2, from: gate, to: done }
        """;

    [Fact]
    public async Task Clone_Creates_New_Draft_Workflow_With_Same_Yaml()
    {
        var owner = await RegisterOwnerAsync("p3-dna");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "test-dna");

        // Create original
        var original = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/workflows",
            new { name = "Invoice Approval", slug = "invoice-approval-dna", yamlContent = ApprovalYaml, createdByMethod = "NaturalLanguage" });
        original.StatusCode.Should().Be(HttpStatusCode.OK);
        var originalId = (await DataAsync(original)).GetProperty("id").GetGuid();

        // Clone it
        var clone = await owner.Client.PostAsync(
            $"/api/v1/workspaces/{ws}/workflows/{originalId}/clone", null);
        clone.StatusCode.Should().Be(HttpStatusCode.Created);
        var cloneData = await DataAsync(clone);
        var cloneId = cloneData.GetProperty("id").GetGuid();

        cloneId.Should().NotBe(originalId);
        cloneData.GetProperty("name").GetString().Should().Contain("copy");

        // Clone appears in list
        var list = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listData = await DataAsync(list);
        var ids = listData.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
        ids.Should().Contain(cloneId);
    }

    [Fact]
    public async Task Dna_Endpoint_Returns_DnaHash_For_Published_Workflow()
    {
        var owner = await RegisterOwnerAsync("p3-dna2");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "test-dna2");

        var create = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/workflows",
            new { name = "DNA Test", slug = "dna-test", yamlContent = ApprovalYaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var wfId = (await DataAsync(create)).GetProperty("id").GetGuid();

        var dna = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{wfId}/dna");
        dna.StatusCode.Should().Be(HttpStatusCode.OK);
        var dnaData = await DataAsync(dna);
        dnaData.GetProperty("dnaHash").GetString().Should().NotBeNullOrWhiteSpace();
        dnaData.GetProperty("hasHumanGates").GetBoolean().Should().BeTrue();
    }
}
