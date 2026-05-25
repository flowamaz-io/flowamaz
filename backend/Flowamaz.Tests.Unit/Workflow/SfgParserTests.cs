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
}
