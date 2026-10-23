using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Application.Workflow.Validation;
using Flowamaz.Application.Workflow.Validators;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using YamlDotNet.RepresentationModel;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

public sealed class VisualInputServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly ModelConfig TestModel = new("claude-sonnet-4-6", "anthropic",
        AiKeySource.Platform, "key", true, 200000, true, true);

    private const string ValidParseJson = """
        {
          "nodes": [
            {"id":"n1","label":"Submit Request","type":"trigger","x":100,"y":100,"confidence":0.98,"annotations":["Typeform"]},
            {"id":"n2","label":"Amount > $5000?","type":"router","x":100,"y":200,"confidence":0.94,"annotations":[]},
            {"id":"n3","label":"Manager Approval","type":"human-gate","x":100,"y":300,"confidence":0.91,"annotations":[]},
            {"id":"n4","label":"Low Conf Step","type":"action","x":200,"y":300,"confidence":0.70,"annotations":["SAP"],"reason":"Handwriting is smudged"},
            {"id":"n5","label":"End","type":"end","x":100,"y":400,"confidence":0.99,"annotations":[]}
          ],
          "edges": [
            {"from":"n1","to":"n2","label":null},
            {"from":"n2","to":"n3","label":"amount > 5000"},
            {"from":"n2","to":"n4","label":"amount <= 5000"},
            {"from":"n3","to":"n5","label":null},
            {"from":"n4","to":"n5","label":null}
          ],
          "low_confidence": ["n4"]
        }
        """;

    // ─── One-shot (no review) ────────────────────────────────────────────────

    [Fact]
    public async Task ProcessImageAsync_LowConfidenceNode_IsIncludedWithReviewNeededFlag()
    {
        var (sut, _) = BuildSut(ValidParseJson);

        var result = await sut.ProcessImageAsync([1, 2, 3], "image/jpeg", WorkspaceId);

        result.YamlDraft.Should().Contain("apiVersion: flowamaz/v1");
        var n4 = FindNode(result.YamlDraft, "n4");
        n4.Should().NotBeNull("low-confidence nodes must no longer be dropped");
        var config = (YamlMappingNode)n4!["config"];
        ((YamlScalarNode)config["review_needed"]).Value.Should().Be("true");
        ((YamlScalarNode)config["review_reason"]).Value.Should().Be("Handwriting is smudged");

        var n1Config = (YamlMappingNode)FindNode(result.YamlDraft, "n1")!["config"];
        n1Config.Children.Keys.Should().NotContain(k => ((YamlScalarNode)k).Value == "review_needed");
    }

    [Fact]
    public async Task ProcessImageAsync_LowConfidenceNode_AppearsInLowConfidenceListWithReason()
    {
        var (sut, _) = BuildSut(ValidParseJson);

        var result = await sut.ProcessImageAsync([1, 2, 3], "image/jpeg", WorkspaceId);

        result.LowConfidenceElements.Should().ContainSingle();
        result.LowConfidenceElements[0].Id.Should().Be("n4");
        result.LowConfidenceElements[0].Confidence.Should().BeLessThan(0.80);
        result.LowConfidenceElements[0].Reason.Should().Be("Handwriting is smudged");
    }

    [Fact]
    public async Task ProcessImageAsync_OneShotYaml_PassesRealValidator()
    {
        var (sut, _) = BuildSut(ValidParseJson);

        var result = await sut.ProcessImageAsync([1], "image/jpeg", WorkspaceId);
        var validation = await new WorkflowValidator(NullLogger<WorkflowValidator>.Instance).ValidateAsync(result.YamlDraft, WorkspaceId);

        validation.IsValid.Should().BeTrue(string.Join("; ", validation.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task ProcessImageAsync_SapAnnotation_MappedToSuggestedConnector()
    {
        var sapNodeJson = """
            {"nodes":[{"id":"n1","label":"SAP Step","type":"action","x":100,"y":100,"confidence":0.95,"annotations":["SAP","ERP"]}],
             "edges":[],"low_confidence":[]}
            """;
        var (sut, _) = BuildSut(sapNodeJson);

        var result = await sut.ProcessImageAsync([1], "image/jpeg", WorkspaceId);

        result.YamlDraft.Should().Contain("suggested_connectors");
        result.YamlDraft.Should().Contain("sap-s4hana");
    }

    [Fact]
    public async Task ProcessImageAsync_MalformedJson_ReturnsEmptyYaml()
    {
        var (sut, _) = BuildSut("not valid json at all {{{");

        var result = await sut.ProcessImageAsync([1], "image/jpeg", WorkspaceId);

        result.YamlDraft.Should().BeEmpty();
        result.LowConfidenceElements.Should().BeEmpty();
    }

    // ─── Review step: preview ────────────────────────────────────────────────

    [Fact]
    public async Task PreviewImageAsync_ReturnsAllNodes_FlagsLowConfidenceWithReason()
    {
        var (sut, metering) = BuildSut(ValidParseJson);

        var preview = await sut.PreviewImageAsync([1], "image/jpeg", WorkspaceId);

        preview.Nodes.Should().HaveCount(5);
        preview.ConfidenceThreshold.Should().Be(0.80);
        preview.Edges.Should().HaveCount(5);
        var low = preview.Nodes.Single(n => n.Id == "n4");
        low.ReviewNeeded.Should().BeTrue();
        low.Reason.Should().Be("Handwriting is smudged");
        low.SuggestedConnectors.Should().Contain("sap-s4hana");
        preview.Nodes.Where(n => n.Id != "n4").Should().OnlyContain(n => !n.ReviewNeeded && n.Reason == null);
        metering.Verify(m => m.RecordUsage(AiFunctionIds.VisualInput, TestModel.ModelId, TestModel.Provider, null, WorkspaceId,
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Once);
    }

    [Fact]
    public async Task PreviewImageAsync_NoReasonFromAi_UsesActionableFallback_AndAcceptsConfidenceScoreKey()
    {
        const string json = """
            {"nodes":[{"id":"Step 1","label":"Check stock","type":"action","confidence_score":0.55,"annotations":[]}],"edges":[]}
            """;
        var (sut, _) = BuildSut(json);

        var preview = await sut.PreviewImageAsync([1], "image/png", WorkspaceId);

        var node = preview.Nodes.Single();
        node.Id.Should().Be("step-1", "ids are sanitised to the schema pattern ^[a-z0-9-]+$");
        node.Confidence.Should().Be(0.55);
        node.ReviewNeeded.Should().BeTrue();
        node.Reason.Should().Contain("55").And.Contain("Confirm the name or remove it");
    }

    [Fact]
    public async Task PreviewImageAsync_UnknownType_BecomesActionAndNeedsReview()
    {
        const string json = """
            {"nodes":[{"id":"a","label":"Cloud","type":"database","confidence":0.97,"annotations":[]}],"edges":[]}
            """;
        var (sut, _) = BuildSut(json);

        var node = (await sut.PreviewImageAsync([1], "image/png", WorkspaceId)).Nodes.Single();

        node.Type.Should().Be("action");
        node.ReviewNeeded.Should().BeTrue();
        node.Reason.Should().Contain("database");
    }

    [Fact]
    public async Task PreviewImageAsync_DuplicateIds_AreMadeUnique_AndEdgesToUnknownNodesDropped()
    {
        const string json = """
            {"nodes":[{"id":"a","label":"One","type":"trigger","confidence":0.9},{"id":"A","label":"Two","type":"end","confidence":0.9}],
             "edges":[{"from":"a","to":"A"},{"from":"a","to":"ghost"}]}
            """;
        var (sut, _) = BuildSut(json);

        var preview = await sut.PreviewImageAsync([1], "image/png", WorkspaceId);

        preview.Nodes.Select(n => n.Id).Should().Equal("a", "a-2");
        preview.Edges.Should().ContainSingle().Which.Should().Be(new DetectedEdge("a", "a-2"));
    }

    // ─── Review step: confirmed graph → YAML ─────────────────────────────────

    [Fact]
    public async Task BuildYamlFromConfirmedGraph_RemovedNode_IsBridged_AndRenameApplied_AndValidates()
    {
        var (sut, _) = BuildSut(ValidParseJson);
        var graph = new ConfirmedVisualGraph(
            [
                new ConfirmedVisualNode("start", "Request submitted", "trigger"),
                new ConfirmedVisualNode("check", "Check budget (renamed)", "action", ["sap-s4hana"]),
                new ConfirmedVisualNode("end", "Done", "end"),
            ],
            [
                new DetectedEdge("start", "smudge"),          // 'smudge' was removed in review
                new DetectedEdge("smudge", "check", "ok"),
                new DetectedEdge("check", "end"),
            ]);

        var yaml = sut.BuildYamlFromConfirmedGraph(graph, "budget-check-abc123", "Budget check");

        FindNode(yaml, "smudge").Should().BeNull();
        ((YamlScalarNode)FindNode(yaml, "check")!["label"]).Value.Should().Be("Check budget (renamed)");
        yaml.Should().NotContain("review_needed");
        var edges = Edges(yaml);
        edges.Should().Contain(("start", "check", "ok"));
        edges.Should().Contain(("check", "end", null));
        edges.Should().NotContain(e => e.From == "smudge" || e.To == "smudge");

        var validation = await new WorkflowValidator(NullLogger<WorkflowValidator>.Instance).ValidateAsync(yaml, WorkspaceId);
        validation.IsValid.Should().BeTrue(string.Join("; ", validation.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task BuildYamlFromConfirmedGraph_NoTriggerOrEnd_AddsThemWiredToEntryAndExit()
    {
        var (sut, _) = BuildSut(ValidParseJson);
        var graph = new ConfirmedVisualGraph(
            [new ConfirmedVisualNode("a", "Collect form", "action"), new ConfirmedVisualNode("b", "File it", "action")],
            [new DetectedEdge("a", "b")]);

        var yaml = sut.BuildYamlFromConfirmedGraph(graph, "wf-1", "Wf");

        ((YamlScalarNode)FindNode(yaml, "start")!["type"]).Value.Should().Be("trigger");
        ((YamlScalarNode)FindNode(yaml, "end")!["type"]).Value.Should().Be("end");
        Edges(yaml).Should().Contain(("start", "a", null)).And.Contain(("b", "end", null));
        (await new WorkflowValidator(NullLogger<WorkflowValidator>.Instance).ValidateAsync(yaml, WorkspaceId)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void BuildYamlFromConfirmedGraph_NameWithYamlMetacharacters_RoundTripsExactly()
    {
        var (sut, _) = BuildSut(ValidParseJson);
        const string name = "Ops \"urgent\": refunds #1\nkind: Evil";
        var graph = new ConfirmedVisualGraph(
            [new ConfirmedVisualNode("s", "Start: \"now\"", "trigger"), new ConfirmedVisualNode("e", "End", "end")],
            [new DetectedEdge("s", "e")]);

        var yaml = sut.BuildYamlFromConfirmedGraph(graph, "ops-1", name);

        var root = Load(yaml);
        ((YamlScalarNode)root["kind"]).Value.Should().Be("Workflow");
        ((YamlScalarNode)((YamlMappingNode)root["metadata"])["name"]).Value.Should().Be(name);
        ((YamlScalarNode)FindNode(yaml, "s")!["label"]).Value.Should().Be("Start: \"now\"");
    }

    [Fact]
    public void BridgeRemovedNodes_ChainOfRemovedNodes_ConnectsSurvivors()
    {
        var edges = new List<DetectedEdge> { new("a", "x"), new("x", "y"), new("y", "b"), new("x", "b") };

        var bridged = VisualInputService.BridgeRemovedNodes(edges, new HashSet<string> { "a", "b" });

        bridged.Should().ContainSingle().Which.Should().Be(new DetectedEdge("a", "b"));
    }

    [Fact]
    public void BridgeRemovedNodes_RemovedSink_DropsEdgeWithoutReplacement()
    {
        var bridged = VisualInputService.BridgeRemovedNodes([new("a", "b"), new("b", "gone")], new HashSet<string> { "a", "b" });

        bridged.Should().Equal(new DetectedEdge("a", "b"));
    }

    // ─── Confirmed graph validator ───────────────────────────────────────────

    [Fact]
    public void ConfirmedGraphValidator_EmptyNodes_FailsWithActionableMessage()
    {
        var result = new ConfirmedVisualGraphValidator().Validate(new ConfirmedVisualGraph([], []));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Keep at least one step"));
    }

    [Fact]
    public void ConfirmedGraphValidator_BlankLabelAndUnknownType_Fail()
    {
        var result = new ConfirmedVisualGraphValidator().Validate(new ConfirmedVisualGraph(
            [new ConfirmedVisualNode("a", " ", "trigger"), new ConfirmedVisualNode("b", "B", "database")], []));

        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("needs a name"));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("unknown type 'database'"));
    }

    [Fact]
    public void ConfirmedGraphValidator_ValidGraph_Passes()
    {
        var result = new ConfirmedVisualGraphValidator().Validate(new ConfirmedVisualGraph(
            [new ConfirmedVisualNode("a", "A", "trigger"), new ConfirmedVisualNode("b", "B", "end")],
            [new DetectedEdge("a", "b")]));

        result.IsValid.Should().BeTrue();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static YamlMappingNode Load(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlMappingNode? FindNode(string yaml, string id)
    {
        var spec = (YamlMappingNode)Load(yaml)["spec"];
        return ((YamlSequenceNode)spec["nodes"]).Children.Cast<YamlMappingNode>()
            .FirstOrDefault(n => ((YamlScalarNode)n["id"]).Value == id);
    }

    private static List<(string From, string To, string? Via)> Edges(string yaml)
    {
        var spec = (YamlMappingNode)Load(yaml)["spec"];
        return ((YamlSequenceNode)spec["edges"]).Children.Cast<YamlMappingNode>()
            .Select(e => (((YamlScalarNode)e["from"]).Value!, ((YamlScalarNode)e["to"]).Value!,
                e.Children.ContainsKey("via") ? ((YamlScalarNode)e["via"]).Value : null))
            .ToList();
    }

    private static (VisualInputService sut, Mock<IAiTokenMeteringService> metering) BuildSut(string aiResponse)
    {
        var ai = new Mock<IAiCompletionService>();
        var models = new Mock<IModelResolutionService>();
        var metering = new Mock<IAiTokenMeteringService>();
        var validator = new Mock<IWorkflowValidator>();

        models.Setup(x => x.ResolveModelConfigAsync(AiFunctionIds.VisualInput, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestModel);
        ai.Setup(x => x.CompleteWithImageAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(aiResponse, 500, 300));

        var sut = new VisualInputService(ai.Object, models.Object, metering.Object, validator.Object,
            NullLogger<VisualInputService>.Instance);
        return (sut, metering);
    }
}
