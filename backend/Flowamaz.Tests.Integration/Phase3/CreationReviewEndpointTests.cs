using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;
using YamlDotNet.RepresentationModel;

namespace Flowamaz.Tests.Integration.Phase3;

/// <summary>
/// Review steps before creation: POST /workflows/visual/preview + /create with a confirmed visualGraph,
/// and POST /workflows/clarify for NL questions. Test host runs with UseStubCompletion=true: the vision
/// stub returns a 3-node graph (Start → Process → End, all high confidence) and the text stub returns
/// non-JSON, so NL clarification is decided by heuristics alone.
/// </summary>
[Collection("api")]
public class CreationReviewEndpointTests : ApiTestBase
{
    // 1x1 transparent PNG — the stub ignores image content.
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    public CreationReviewEndpointTests(IntegrationApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task VisualPreview_ReturnsDetectedGraph_AndPersistsNothing()
    {
        var owner = await RegisterOwnerAsync("rv-preview");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-preview");

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/visual/preview",
            new { imageBase64 = TinyPng, imageMimeType = "image/png" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = await DataAsync(response);
        data.GetProperty("confidenceThreshold").GetDouble().Should().Be(0.8);
        var nodes = data.GetProperty("nodes").EnumerateArray().ToList();
        nodes.Select(n => n.GetProperty("id").GetString()).Should().Equal("n1", "n2", "n3");
        nodes.Should().OnlyContain(n => n.GetProperty("reviewNeeded").GetBoolean() == false);
        nodes[0].GetProperty("confidence").GetDouble().Should().BeGreaterThan(0.8);
        data.GetProperty("edges").GetArrayLength().Should().Be(2);

        var list = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows");
        (await list.Content.ReadAsStringAsync()).Should().NotContain("Visual Workflow");
    }

    [Fact]
    public async Task VisualPreview_InvalidBase64_Returns400WithActionableMessage()
    {
        var owner = await RegisterOwnerAsync("rv-badimg");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-badimg");

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/visual/preview",
            new { imageBase64 = "%%%not-base64%%%", imageMimeType = "image/png" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Re-encode the image");
    }

    [Fact]
    public async Task VisualCreate_WithConfirmedGraph_RemovedAndRenamedNodes_SavesBridgedYaml()
    {
        var owner = await RegisterOwnerAsync("rv-confirm");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-confirm");
        const string name = "Whiteboard intake";

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create", new
        {
            name,
            method = "visual",
            visualGraph = new
            {
                nodes = new object[]
                {
                    new { id = "n1", label = "Form received", type = "trigger" },
                    new { id = "n3", label = "Archive", type = "action", suggestedConnectors = new[] { "slack" } },
                    new { id = "n4", label = "End", type = "end" },
                },
                edges = new object[]
                {
                    new { from = "n1", to = "n2" },   // n2 removed during review → bridged n1 → n3
                    new { from = "n2", to = "n3" },
                    new { from = "n3", to = "n4" },
                },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var created = await DataAsync(response);
        created.GetProperty("method").GetString().Should().Be("visual");
        var id = created.GetProperty("workflowId").GetGuid();

        var yaml = await GetYamlAsync(owner.Client, ws, id);
        var root = Load(yaml);
        ((YamlScalarNode)((YamlMappingNode)root["metadata"])["name"]).Value.Should().Be(name);
        var spec = (YamlMappingNode)root["spec"];
        var nodeIds = ((YamlSequenceNode)spec["nodes"]).Children.Cast<YamlMappingNode>()
            .Select(n => ((YamlScalarNode)n["id"]).Value).ToList();
        nodeIds.Should().Equal("n1", "n3", "n4");
        var edges = ((YamlSequenceNode)spec["edges"]).Children.Cast<YamlMappingNode>()
            .Select(e => $"{((YamlScalarNode)e["from"]).Value}->{((YamlScalarNode)e["to"]).Value}").ToList();
        edges.Should().BeEquivalentTo("n1->n3", "n3->n4");
        yaml.Should().Contain("Form received").And.NotContain("review_needed");
    }

    [Fact]
    public async Task VisualCreate_WithConfirmedGraphThatIsInvalid_Returns422_AndPersistsNothing()
    {
        var owner = await RegisterOwnerAsync("rv-invalid");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-invalid");
        const string name = "Two Trigger Sketch";

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create", new
        {
            name,
            method = "visual",
            visualGraph = new
            {
                nodes = new object[]
                {
                    new { id = "a", label = "Start A", type = "trigger" },
                    new { id = "b", label = "Start B", type = "trigger" },
                    new { id = "c", label = "Done", type = "end" },
                },
                edges = new object[] { new { from = "a", to = "c" }, new { from = "b", to = "c" } },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKFLOW_GENERATION_INVALID");

        var list = await owner.Client.GetAsync($"/api/v1/workspaces/{ws}/workflows");
        (await list.Content.ReadAsStringAsync()).Should().NotContain(name);
    }

    [Fact]
    public async Task VisualCreate_WithAllNodesRemoved_Returns400()
    {
        var owner = await RegisterOwnerAsync("rv-empty");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-empty");

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create", new
        {
            name = "Nothing kept",
            method = "visual",
            visualGraph = new { nodes = Array.Empty<object>(), edges = Array.Empty<object>() },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("start from the canvas");
    }

    [Fact]
    public async Task VisualCreate_OneShotImageWithoutReview_StillCreates()
    {
        var owner = await RegisterOwnerAsync("rv-oneshot");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-oneshot");

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create", new
        {
            name = "One shot diagram",
            method = "visual",
            imageBase64 = TinyPng,
            imageMimeType = "image/png",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var id = (await DataAsync(response)).GetProperty("workflowId").GetGuid();
        var yaml = await GetYamlAsync(owner.Client, ws, id);
        yaml.Should().Contain("Process");
    }

    [Fact]
    public async Task Clarify_MissingEssentials_ReturnsHeuristicQuestions_WithStubAi()
    {
        var owner = await RegisterOwnerAsync("rv-clarify");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-clarify");

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/clarify", new
        {
            workflowName = "Expense claims",
            purpose = "Handle expense claims",
            triggerDescription = "",
            stepsDescription = "Check the claim.\nIt needs approval.\nPay the claim.",
            rulesAndConstraints = "",
            systemsAndAi = "",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = await DataAsync(response);
        data.GetProperty("usedAi").GetBoolean().Should().BeFalse();
        var topics = data.GetProperty("questions").EnumerateArray().Select(q => q.GetProperty("topic").GetString()).ToList();
        topics.Should().Equal("trigger", "approver", "rejection");
        data.GetProperty("questions")[0].GetProperty("question").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Clarify_CompleteDescription_ReturnsNoQuestions()
    {
        var owner = await RegisterOwnerAsync("rv-clarify-ok");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-clarify-ok");

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/clarify", new
        {
            workflowName = "Purchase Approval",
            purpose = "Turn purchase requests into purchase orders",
            triggerDescription = "An employee submits the purchase request form",
            stepsDescription = "Validate the requester.\nThe line manager approves it.\nIf rejected, notify the requester.",
            rulesAndConstraints = "48 hour SLA",
            systemsAndAi = "SAP, Slack",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await DataAsync(response)).GetProperty("questions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Clarify_MissingStepsDescription_Returns400()
    {
        var owner = await RegisterOwnerAsync("rv-clarify-bad");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-clarify-bad");

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/clarify", new
        {
            workflowName = "X workflow",
            purpose = "p",
            triggerDescription = "t",
            stepsDescription = "",
            rulesAndConstraints = "",
            systemsAndAi = "",
        });

        // StepsDescription is non-nullable, so MVC's implicit [Required] rejects a blank value before the
        // action runs (the FluentValidation NotEmpty rule is the backstop for direct service callers).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().ContainEquivalentOf("StepsDescription");
    }

    [Fact]
    public async Task Create_Nl_WithClarificationAnswers_IsAcceptedAndReachesGenerationGate()
    {
        // The text stub never returns YAML, so generation still ends at the 422 gate — this proves the
        // clarifications field binds, validates and flows into generation without changing the gate.
        var owner = await RegisterOwnerAsync("rv-answers");
        var ws = await CreateWorkspaceAsync(owner.Client, "Test", "rv-answers");

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create", new
        {
            name = "Expense claims",
            method = "nl",
            nlRequest = new
            {
                workflowName = "Expense claims",
                purpose = "Handle expense claims",
                triggerDescription = "",
                stepsDescription = "Check the claim.\nIt needs approval.\nPay the claim.",
                rulesAndConstraints = "",
                systemsAndAi = "",
                clarifications = new[]
                {
                    new { topic = "approver", question = "Who approves this request?", answer = "Line manager" },
                    new { topic = "rejection", question = "What should happen if the request is rejected?", answer = "" },
                },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKFLOW_GENERATION_INVALID");
    }

    private static async Task<string> GetYamlAsync(HttpClient client, Guid ws, Guid id)
    {
        var get = await client.GetAsync($"/api/v1/workspaces/{ws}/workflows/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await DataAsync(get)).GetProperty("yamlContent").GetString()!;
    }

    private static YamlMappingNode Load(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }
}
