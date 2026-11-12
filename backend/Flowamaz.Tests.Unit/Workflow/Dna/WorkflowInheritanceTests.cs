using FluentAssertions;
using Flowamaz.Application.Workflow.Dna;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Workflow;

namespace Flowamaz.Tests.Unit.Workflow.Dna;

/// <summary>DNA inheritance (§8.8): who counts as better, which patterns differ, and the YAML edit.</summary>
public sealed class WorkflowInheritanceTests
{
    internal const string Weak = """
        workflow:
          id: po-west
          version: v1
          name: "PO approval (West)"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          # the request goes to finance
          - id: finance
            type: HumanGate
            label: "Finance approval"
          - id: create-po
            type: Action
            label: "Create PO"
            config:
              connector: "http-rest"
              operation: "request"
          - id: end
            type: End
            label: "End"
        edges:
          - { id: e1, from: start, to: finance }
          - { id: e2, from: finance, to: create-po }
          - { id: e3, from: create-po, to: end }
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

    private static WorkflowMetric Hour(int completed, int failed, long avgMs, int breaches = 0) => new()
    {
        RunsTotal = completed + failed, RunsCompleted = completed, RunsFailed = failed, AvgDurationMs = avgMs, SlaBreachCount = breaches,
    };

    [Fact]
    public void Performance_adds_up_the_hours()
    {
        var p = WorkflowInheritanceService.Performance([Hour(6, 2, 3_600_000), Hour(1, 1, 7_200_000, breaches: 1)]);
        p.Runs.Should().Be(10);
        p.SuccessPercent.Should().Be(70);
        p.SlaCompliancePercent.Should().Be(90);
        p.AvgHours.Should().Be(1.2);
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

        weak.GatesWithoutTimeout.Should().Equal("finance");
        weak.ActionsWithoutRetry.Should().Equal("create-po");
        weak.NotifiesAfterApproval.Should().BeFalse();
        strong.GateTimeout.Should().Be((24, "escalate"));
        strong.ActionRetry!.MaxAttempts.Should().Be(3);
        strong.NotifiesAfterApproval.Should().BeTrue();
        strong.GatesWithoutTimeout.Should().BeEmpty();
    }

    [Fact]
    public void Applying_edits_only_the_named_nodes_and_keeps_the_workflow_readable()
    {
        var yaml = WorkflowInheritanceService.YamlNodes.Edit(Weak, ["finance"], node =>
        {
            var config = WorkflowInheritanceService.YamlNodes.Child(node, "config");
            config.Children[new YamlDotNet.RepresentationModel.YamlScalarNode("timeout_hours")] = new YamlDotNet.RepresentationModel.YamlScalarNode("24");
        });

        var graph = new SfgParser().Parse(yaml);
        graph.FindNode("finance")!.Config.RootElement.GetProperty("timeout_hours").GetInt32().Should().Be(24);
        graph.FindNode("create-po")!.Config.RootElement.GetProperty("connector").GetString().Should().Be("http-rest");
        graph.Edges.Should().HaveCount(3);
        WorkflowInheritanceService.Patterns.Of(graph).GatesWithoutTimeout.Should().BeEmpty();
    }
}
