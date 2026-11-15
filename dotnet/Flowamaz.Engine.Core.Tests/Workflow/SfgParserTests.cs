using FluentAssertions;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Workflow;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Covers the SFG parser's structural validation (prompt 02-02): a sound graph parses to the right
/// node/edge counts, and each invalid shape throws an actionable <see cref="SfgParseException"/>.
/// </summary>
public class SfgParserTests
{
    private readonly SfgParser _parser = new();

    private const string ValidYaml = """
        workflow:
          id: order-flow
          version: v1
          name: Order Flow
        nodes:
          - id: start
            type: Trigger
            label: Start
          - id: process
            type: Action
            label: Process
            config:
              url: https://example.test
          - id: done
            type: End
            label: Done
        edges:
          - id: e1
            from: start
            to: process
          - id: e2
            from: process
            to: done
        """;

    [Fact]
    public void Parse_valid_graph_returns_expected_nodes_and_edges()
    {
        var graph = _parser.Parse(ValidYaml);

        graph.Nodes.Should().HaveCount(3);
        graph.Edges.Should().HaveCount(2);
        graph.TriggerNode.Id.Should().Be("start");
        graph.FindNode("process")!.Type.Should().Be(NodeType.Action);
        graph.FindNode("process")!.Config.RootElement.GetProperty("url").GetString()
            .Should().Be("https://example.test");
    }

    [Fact]
    public void Parse_missing_trigger_throws()
    {
        const string yaml = """
            nodes:
              - id: a
                type: Action
              - id: done
                type: End
            edges:
              - { id: e1, from: a, to: done }
            """;

        var act = () => _parser.Parse(yaml);
        act.Should().Throw<SfgParseException>().WithMessage("*exactly one Trigger*");
    }

    [Fact]
    public void Parse_no_end_node_throws()
    {
        const string yaml = """
            nodes:
              - { id: start, type: Trigger }
              - { id: a, type: Action }
            edges:
              - { id: e1, from: start, to: a }
            """;

        var act = () => _parser.Parse(yaml);
        act.Should().Throw<SfgParseException>().WithMessage("*End node*");
    }

    [Fact]
    public void Parse_orphaned_node_throws_naming_the_node()
    {
        const string yaml = """
            nodes:
              - { id: start, type: Trigger }
              - { id: process, type: Action }
              - { id: done, type: End }
              - { id: lonely, type: Action }
            edges:
              - { id: e1, from: start, to: process }
              - { id: e2, from: process, to: done }
            """;

        var act = () => _parser.Parse(yaml);
        act.Should().Throw<SfgParseException>().WithMessage("*lonely*");
    }

    [Fact]
    public void Parse_edge_to_unknown_node_throws()
    {
        const string yaml = """
            nodes:
              - { id: start, type: Trigger }
              - { id: done, type: End }
            edges:
              - { id: e1, from: start, to: ghost }
            """;

        var act = () => _parser.Parse(yaml);
        act.Should().Throw<SfgParseException>().WithMessage("*ghost*does not exist*");
    }

    [Fact]
    public void Parse_unknown_node_type_throws()
    {
        const string yaml = """
            nodes:
              - { id: start, type: Trigger }
              - { id: weird, type: Frobnicate }
              - { id: done, type: End }
            edges:
              - { id: e1, from: start, to: weird }
              - { id: e2, from: weird, to: done }
            """;

        var act = () => _parser.Parse(yaml);
        act.Should().Throw<SfgParseException>().WithMessage("*unknown type*Frobnicate*");
    }

    [Fact]
    public void Parse_empty_yaml_throws()
    {
        var act = () => _parser.Parse("   ");
        act.Should().Throw<SfgParseException>();
    }

    // ── flowamaz/v1 envelope (templates/exports): metadata: + spec.nodes/spec.edges with
    //    hyphenated node types. Runtime must parse this identically to the legacy root-level form.
    private const string SpecFormatYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: spec-flow
          name: Spec Flow
          version: "1.0.0"
        spec:
          nodes:
            - id: start
              type: trigger
              label: Start
            - id: gate
              type: human-gate
              label: Approve
            - id: done
              type: end
              label: Done
          edges:
            - id: e1
              from: start
              to: gate
            - id: e2
              from: gate
              to: done
        """;

    [Fact]
    public void Parse_spec_envelope_reads_nodes_edges_and_hyphenated_types()
    {
        var graph = _parser.Parse(SpecFormatYaml);

        graph.Nodes.Should().HaveCount(3);
        graph.Edges.Should().HaveCount(2);
        graph.TriggerNode.Id.Should().Be("start");
        graph.FindNode("gate")!.Type.Should().Be(NodeType.HumanGate);
    }

    [Fact]
    public void CanvasRouterBranches_AreCopiedOntoTheirEdges_AndDefaultIsFlagged()
    {
        // The canvas keeps branch conditions on the router and links edges by label (via).
        const string yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata: { id: r, name: R }
            spec:
              nodes:
                - { id: start, type: trigger }
                - id: route
                  type: router
                  config:
                    branches:
                      - { label: Large, condition: "${amount} > 1000" }
                      - { label: Otherwise, condition: "", isDefault: true }
                - { id: big, type: end }
                - { id: small, type: end }
              edges:
                - { id: e1, from: start, to: route }
                - { id: e2, from: route, to: big, via: Large }
                - { id: e3, from: route, to: small, via: Otherwise }
            """;

        var graph = new SfgParser().Parse(yaml);

        graph.Edges.Single(e => e.Id == "e2").Condition.Should().Be("${amount} > 1000");
        graph.Edges.Single(e => e.Id == "e2").IsDefault.Should().BeFalse();
        graph.Edges.Single(e => e.Id == "e3").IsDefault.Should().BeTrue();
        graph.Edges.Single(e => e.Id == "e1").Condition.Should().BeNull();
    }

    [Fact]
    public void NodeConfig_PlainScalars_AreTyped_QuotedStaysString()
    {
        // Regression: untyped YAML scalars became JSON strings, so GetInt/boolean reads in workers
        // (timeout_hours, max_tokens, isDefault, channels) silently fell back to defaults.
        const string yaml = """
            nodes:
              - { id: start, type: Trigger }
              - id: gate
                type: HumanGate
                config: { timeout_hours: 72, notify: true, ratio: 0.5, version: "1.0", code: "007" }
              - { id: done, type: End }
            edges:
              - { from: start, to: gate }
              - { from: gate, to: done }
            """;

        var config = new SfgParser().Parse(yaml).FindNode("gate")!.Config.RootElement;

        config.GetProperty("timeout_hours").GetInt32().Should().Be(72);
        config.GetProperty("notify").GetBoolean().Should().BeTrue();
        config.GetProperty("ratio").GetDouble().Should().Be(0.5);
        config.GetProperty("version").GetString().Should().Be("1.0");
        config.GetProperty("code").GetString().Should().Be("007");
    }
}
