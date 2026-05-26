using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;

namespace Flowamaz.Tests.Integration.Phase3;

/// <summary>
/// Phase 3 creation pipeline integration tests.
/// Uses the real HTTP stack with mocked AI (UseStubCompletion=true in test host).
/// Validates: NL→YAML generate, caching, YAML validation, co-pilot rate limits.
/// </summary>
[Collection("api")]
public class Phase3CreationTests : ApiTestBase
{
    public Phase3CreationTests(IntegrationApiFixture fixture) : base(fixture) { }

    private static readonly object NlRequest = new
    {
        workflowName = "Invoice Approval",
        purpose = "Automate invoice approvals over $1000",
        triggerDescription = "Finance team member submits invoice",
        stepsDescription = "1. Validate invoice\n2. Route to manager\n3. Manager approves or rejects",
        rulesAndConstraints = "Only invoices over $1000 require approval",
        systemsAndAi = "SAP, Slack",
    };

    private const string ValidYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: invoice-approval
          name: "Invoice Approval"
          version: "1.0.0"
        spec:
          trigger:
            type: manual
          nodes:
            - id: start
              type: trigger
              label: "Start"
              config: {}
            - id: approve
              type: human-gate
              label: "Manager Approval"
              config:
                assignees: []
            - id: end
              type: end
              label: "End"
              config: {}
          edges:
            - id: e1
              from: start
              to: approve
            - id: e2
              from: approve
              to: end
        """;

    [Fact]
    public async Task Generate_Returns_Yaml_From_NL_Request()
    {
        var owner = await RegisterOwnerAsync("p3-gen");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "test-gen");

        // The SSE endpoint streams — just read the raw response body and look for the done event
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/workspaces/{ws}/workflows/generate");
        request.Content = JsonContent.Create(NlRequest);
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await owner.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"type\":\"done\"");
    }

    [Fact]
    public async Task Validate_Valid_Yaml_Returns_IsValid_True()
    {
        var owner = await RegisterOwnerAsync("p3-val");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "test-val");

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/workflows/validate",
            new { yaml_content = ValidYaml });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        data.GetProperty("isValid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Orphaned_Node_Returns_Layer2_Error()
    {
        var owner = await RegisterOwnerAsync("p3-orphan");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "test-orphan");

        var orphanYaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              id: orphan-test
              name: "Orphan Test"
              version: "1.0.0"
            spec:
              trigger:
                type: manual
              nodes:
                - id: start
                  type: trigger
                  label: "Start"
                  config: {}
                - id: orphan
                  type: action
                  label: "Orphan Node"
                  config: {}
                - id: end
                  type: end
                  label: "End"
                  config: {}
              edges:
                - id: e1
                  from: start
                  to: end
            """;

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/workflows/validate",
            new { yaml_content = orphanYaml });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        // Orphaned node should appear in errors or warnings
        var errorsOrWarnings = data.TryGetProperty("errors", out var e)
            ? e.GetArrayLength() + (data.TryGetProperty("warnings", out var w) ? w.GetArrayLength() : 0)
            : 0;
        errorsOrWarnings.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Copilot_Pattern_Match_Returns_Patch_Without_Ai()
    {
        var owner = await RegisterOwnerAsync("p3-cop");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "test-cop");

        // First create a workflow to get a valid ID
        var create = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/workflows",
            new { name = "Copilot Test", slug = "copilot-test", yamlContent = ValidYaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        var copilot = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/workflows/{workflowId}/copilot",
            new { command = "add a 24h timeout to manager-gate", yaml_content = ValidYaml });

        copilot.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(copilot);
        // Pattern should match — yaml_patch should be non-null
        data.TryGetProperty("yamlPatch", out var patch);
        patch.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Copilot_Rate_Limit_Returns_429_After_60_Calls()
    {
        var owner = await RegisterOwnerAsync("p3-rate");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "test-rate");

        var create = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/workflows",
            new { name = "Rate Test", slug = "rate-test", yamlContent = ValidYaml, createdByMethod = "NaturalLanguage" });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflowId = (await DataAsync(create)).GetProperty("id").GetGuid();

        // Make 60 calls (all should succeed — pattern match returns immediately)
        for (var i = 0; i < 60; i++)
        {
            var resp = await owner.Client.PostAsJsonAsync(
                $"/api/v1/workspaces/{ws}/workflows/{workflowId}/copilot",
                new { command = $"add step Step{i}", yaml_content = ValidYaml });
            if (resp.StatusCode == HttpStatusCode.TooManyRequests) break;
        }

        // 61st call should be rate limited
        var final = await owner.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{ws}/workflows/{workflowId}/copilot",
            new { command = "add step Final", yaml_content = ValidYaml });

        final.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
