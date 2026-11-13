using FluentAssertions;
using Flowamaz.Application.Workflow.Dna;
using Flowamaz.Core.Workflow;

namespace Flowamaz.Tests.Unit.Workflow.Dna;

/// <summary>DNA inheritance (§8.8): who counts as better, which patterns differ, and the YAML edit.</summary>
public sealed class WorkflowInheritanceTests
{
    // The v1 envelope (spec.nodes), as NL generation and the canvas write it.
    internal const string Weak = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: po-west
          name: "PO approval (West)"
        spec:
          trigger:
            type: manual
          nodes:
            - id: start
              type: Trigger
              label: "Start"
            # the request goes to finance
            - id: finance
              type: HumanGate
              label: "Finance approval"
            - id: legal
              type: HumanGate
              label: "Legal review"
              config:
                timeoutValue: 3
                timeoutUnit: days
                onTimeout: auto-reject
            - id: create-po
              type: Action
              label: "Create PO"
              config:
                connector: "http-rest"
                operation: "request"
            - id: ping
              type: Action
              label: "Ping requester"
              config:
                connector: "slack"
                operation: "send-message"
            - id: end
              type: End
              label: "End"
          edges:
            - { id: e1, from: start, to: finance }
            - { id: e2, from: finance, to: legal }
            - { id: e3, from: legal, to: create-po }
            - { id: e4, from: create-po, to: ping }
            - { id: e5, from: ping, to: end }
        """;

    internal const string Strong = """
        workflow:
          id: po-east
          version: v1
          name: "PO approval (East)"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          - id: finance
            type: HumanGate
            label: "Finance approval"
            config:
              timeout_hours: 24
              on_timeout: escalate
              escalate_to: cfo@acme.test
          - id: create-po
            type: Action
            label: "Create PO"
            config:
              connector: "http-rest"
              operation: "request"
            retry:
              maxAttempts: 3
              backoffSeconds: 30
              backoffMultiplier: 2
          - id: tell
            type: Action
            label: "Tell requester"
            config:
              connector: "email-smtp"
              operation: "send-email"
          - id: end
            type: End
            label: "End"
        edges:
          - { id: e1, from: start, to: finance }
          - { id: e2, from: finance, to: create-po }
          - { id: e3, from: create-po, to: tell }
          - { id: e4, from: tell, to: end }
        """;

    [Fact]
    public void Performance_comes_from_finished_runs()
    {
        var p = WorkflowInheritanceService.Performance(new Core.Models.RunPerformanceRow(Completed: 7, Failed: 3, AvgDurationMs: 4_320_000, SlaBreaches: 1));
        p.Runs.Should().Be(10);
        p.SuccessPercent.Should().Be(70);
        p.SlaCompliancePercent.Should().Be(90);
        p.AvgHours.Should().Be(1.2);
        WorkflowInheritanceService.Performance(null).Should().Be(new WorkflowPerformance(0, 0, 100, 0));
    }

    [Theory]
    [InlineData(95, 100, 10, 70, 100, 10, "95% of its runs succeed, against 70% here.")]
    [InlineData(90, 98, 10, 90, 80, 10, "It meets its SLA 98% of the time, against 80% here.")]
    [InlineData(90, 100, 3, 90, 100, 10, "Its runs take 3 hours on average, against 10 here.")]
    [InlineData(80, 100, 9, 75, 95, 10, null)]
    public void A_sibling_must_be_clearly_better(int s1, int sla1, double h1, int s2, int sla2, double h2, string? evidence) =>
        WorkflowInheritanceService.Better(new(20, s1, sla1, h1), new(20, s2, sla2, h2)).Should().Be(evidence);

    [Fact]
    public void Patterns_find_what_the_weak_workflow_lacks()
    {
        var weak = WorkflowInheritanceService.Patterns.Of(new SfgParser().Parse(Weak));
        var strong = WorkflowInheritanceService.Patterns.Of(new SfgParser().Parse(Strong));

        weak.GatesWithoutTimeout.Should().Equal(["finance"], "the canvas deadline on 'legal' (3 days) counts, as it does in the engine");
        weak.NotifiesAfterApproval.Should().BeTrue("'ping' follows the approvals");
        strong.GateTimeout.Should().Be(new WorkflowInheritanceService.GateDeadline(24, "escalate", "cfo@acme.test"));
        strong.RetryByConnector["http-rest"].MaxAttempts.Should().Be(3);
        strong.GatesWithoutTimeout.Should().BeEmpty();

        var targets = WorkflowInheritanceService.RetryTargets(strong, weak);
        targets.Select(t => t.NodeId).Should().Equal(["create-po"], "the Slack step never gets a retry: it would message twice");
    }

    [Fact]
    public void Applying_edits_only_the_named_nodes_in_the_v1_envelope()
    {
        var yaml = WorkflowInheritanceService.YamlNodes.Edit(Weak, new HashSet<string> { "finance" }, (_, node) =>
        {
            var config = WorkflowInheritanceService.YamlNodes.Child(node, "config");
            config.Children[new YamlDotNet.RepresentationModel.YamlScalarNode("timeout_hours")] = new YamlDotNet.RepresentationModel.YamlScalarNode("24");
        });

        var graph = new SfgParser().Parse(yaml);
        graph.FindNode("finance")!.Config.RootElement.GetProperty("timeout_hours").GetInt32().Should().Be(24);
        graph.FindNode("legal")!.Config.RootElement.GetProperty("timeoutValue").GetInt32().Should().Be(3, "other steps are untouched");
        graph.Edges.Should().HaveCount(5);
        WorkflowInheritanceService.Patterns.Of(graph).GatesWithoutTimeout.Should().BeEmpty();
    }

    [Fact]
    public void Yaml_with_anchors_is_left_to_the_editor()
    {
        const string withAlias = """
            nodes:
              - id: a
                type: HumanGate
                label: A
                config: &shared { assignee: x }
              - id: b
                type: HumanGate
                label: B
                config: *shared
            """;
        var act = () => WorkflowInheritanceService.YamlNodes.Edit(withAlias, new HashSet<string> { "a" }, (_, _) => { });
        act.Should().Throw<InheritanceNotApplicableException>().WithMessage("*anchors*");
    }
}
