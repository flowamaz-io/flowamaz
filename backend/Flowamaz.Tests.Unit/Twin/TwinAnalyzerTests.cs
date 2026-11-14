using System.Text.Json.Nodes;
using FluentAssertions;
using Flowamaz.Core.Workflow;
using Flowamaz.Core.Workflow.Twin;

namespace Flowamaz.Tests.Unit.Twin;

/// <summary>
/// Workflow Twin (FUNCTIONAL.md §8.1): the simulator routes like the engine, the generator finds the boundary values,
/// and the analyser reports what breaks — e.g. "breaks when amount = 5000: router uses > not ≥".
/// </summary>
public class TwinAnalyzerTests
{
    private static readonly SfgParser Parser = new();

    private const string PurchaseApproval = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: purchase, name: Purchase approval }
        spec:
          variables:
            amount: { type: number, required: true }
          nodes:
            - { id: start, type: trigger }
            - id: size
              type: router
              label: Amount check
              config:
                branches:
                  - { label: Small, condition: "{{ amount }} < 5000" }
                  - { label: Large, condition: "{{ amount }} > 5000" }
            - { id: manager, type: human-gate, label: Manager approval }
            - { id: decided, type: router, label: Approved? }
            - { id: pay, type: action, label: Pay }
            - { id: done, type: end }
          edges:
            - { from: start, to: size }
            - { from: size, to: pay, via: Small }
            - { from: size, to: manager, via: Large }
            - { from: manager, to: decided }
            - { from: decided, to: pay, via: approved }
            - { from: pay, to: done }
        """;

    [Fact]
    public void Finds_the_spec_example_strict_operators_leave_the_boundary_unrouted()
    {
        var report = TwinAnalyzer.Analyse(Parser.Parse(PurchaseApproval), TwinHistory.Empty);

        var boundary = report.Findings.Single(f => f.Kind == "no-route" && f.NodeId == "size" && f.Severity == TwinSeverity.Break
            && f.Title.Contains("5000"));
        boundary.Title.Should().Be("Breaks when amount = 5000");
        boundary.Fix.Should().Contain(">=").And.Contain("exactly 5000");
        boundary.Explanation.Should().Contain("never finishes");
        boundary.Example["amount"].Should().Be("5000");
        report.CasesRun.Should().BeGreaterThan(20);
    }

    [Fact]
    public void A_rejection_with_no_rejected_branch_is_a_break()
    {
        var report = TwinAnalyzer.Analyse(Parser.Parse(PurchaseApproval), TwinHistory.Empty);

        var rejected = report.Findings.Single(f => f.NodeId == "decided" && f.Kind == "no-route");
        rejected.Severity.Should().Be(TwinSeverity.Break);
        rejected.Title.Should().Contain("\"Manager approval\" is rejected");
        rejected.Fix.Should().Contain("rejected");
    }

    [Fact]
    public void A_missing_required_field_and_a_text_number_are_warnings()
    {
        var report = TwinAnalyzer.Analyse(Parser.Parse(PurchaseApproval), TwinHistory.Empty);

        var missing = report.Findings.Single(f => f.NodeId == "size" && f.Title.Contains("missing"));
        missing.Severity.Should().Be(TwinSeverity.Warning);
        missing.Fix.Should().Contain("declared required");

        var text = report.Findings.Single(f => f.NodeId == "size" && f.Title.Contains("\"abc\"") || f.NodeId == "size" && f.Title.Contains("\"5,000\""));
        text.Severity.Should().Be(TwinSeverity.Warning);
    }

    [Fact]
    public void Overlapping_conditions_take_two_branches()
    {
        var yaml = PurchaseApproval.Replace("< 5000", "<= 5000").Replace("> 5000", ">= 5000");
        var report = TwinAnalyzer.Analyse(Parser.Parse(yaml), TwinHistory.Empty);

        var overlap = report.Findings.Single(f => f.Kind == "multiple-routes" && f.NodeId == "size");
        overlap.Severity.Should().Be(TwinSeverity.Break);
        overlap.Title.Should().Contain("amount = 5000");
        report.Findings.Should().NotContain(f => f.Kind == "no-route" && f.NodeId == "size" && f.Severity == TwinSeverity.Break);
    }

    [Fact]
    public void A_correct_router_with_a_default_branch_has_no_findings_there()
    {
        const string yaml = """
            nodes:
              - { id: start, type: trigger }
              - id: size
                type: router
                config:
                  branches:
                    - { label: Small, condition: "{{ amount }} < 5000" }
                    - { label: Rest, isDefault: true }
              - { id: a, type: action }
              - { id: b, type: action }
              - { id: done, type: end }
            edges:
              - { from: start, to: size }
              - { from: size, to: a, via: Small }
              - { from: size, to: b, via: Rest }
              - { from: a, to: done }
              - { from: b, to: done }
            """;
        var report = TwinAnalyzer.Analyse(Parser.Parse(yaml), TwinHistory.Empty);

        report.Findings.Where(f => f.Severity != TwinSeverity.Info).Should().BeEmpty();
        report.Breaks.Should().Be(0);
    }

    [Fact]
    public void A_real_past_payload_that_breaks_is_reported_with_only_the_relevant_values()
    {
        const string yaml = """
            nodes:
              - { id: start, type: trigger }
              - id: region
                type: router
                config:
                  branches:
                    - { label: EU, condition: "{{ region }} == \"EU\"" }
                    - { label: US, condition: "{{ region }} == \"US\"" }
              - { id: done, type: end }
            edges:
              - { from: start, to: region }
              - { from: region, to: done, via: EU }
              - { from: region, to: done, via: US }
            """;
        var history = new TwinHistory([JsonNode.Parse("""{"region":"APAC","customer":"secret-name"}""")], new Dictionary<string, JsonNode?>());
        var report = TwinAnalyzer.Analyse(Parser.Parse(yaml), history);

        var finding = report.Findings.First(f => f.Kind == "no-route" && f.Severity == TwinSeverity.Break);
        finding.Example.Keys.Should().NotContain("customer", "only the values the conditions read are shown");
        report.Findings.Should().Contain(f => f.Title == "Breaks for a recent real payload (#1)");
    }

    [Fact]
    public void Node_outputs_are_simulated_from_recorded_runs_and_varied()
    {
        const string yaml = """
            nodes:
              - { id: start, type: trigger }
              - { id: budget, type: action, label: Check budget }
              - id: enough
                type: if-else
                config:
                  branches:
                    - { label: yes, condition: "{{ budget.output.remaining }} >= 0" }
                    - { label: no, condition: "{{ budget.output.remaining }} < 0" }
              - { id: done, type: end }
            edges:
              - { from: start, to: budget }
              - { from: budget, to: enough }
              - { from: enough, to: done, via: yes }
              - { from: enough, to: done, via: no }
            """;
        var recorded = new Dictionary<string, JsonNode?> { ["budget"] = JsonNode.Parse("""{"remaining":1200}""") };
        var report = TwinAnalyzer.Analyse(Parser.Parse(yaml), new TwinHistory([], recorded));

        report.Findings.Should().NotContain(f => f.Severity == TwinSeverity.Break && f.Kind == "no-route" && f.Title.Contains("= 0"));
        report.Findings.Should().Contain(f => f.NodeId == "enough" && f.Title.Contains("budget.output.remaining is missing"));
    }

    [Fact]
    public void Parser_reads_declared_inputs_and_tolerates_other_shapes()
    {
        Parser.Parse(PurchaseApproval).DeclaredInputs.Should().ContainSingle(i => i.Name == "amount" && i.Type == "number" && i.Required);
        const string listShape = """
            variables: [a, b]
            nodes:
              - { id: start, type: trigger }
              - { id: done, type: end }
            edges:
              - { from: start, to: done }
            """;
        Parser.Parse(listShape).DeclaredInputs.Should().BeEmpty();
    }

    [Fact]
    public void Simulator_matches_the_engine_default_branch_and_decision_rules()
    {
        var graph = Parser.Parse(PurchaseApproval);
        var trace = TwinSimulator.Run(graph, JsonNode.Parse("""{"amount":9000}"""),
            new Dictionary<string, JsonNode?>(), new Dictionary<string, string> { ["manager"] = "approve" });

        trace.ReachedEnd.Should().BeTrue();
        trace.NodesVisited.Should().Contain(["manager", "decided", "pay", "done"]);
        trace.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Generator_caps_the_number_of_cases()
    {
        TwinCaseGenerator.Generate(Parser.Parse(PurchaseApproval), TwinHistory.Empty, maxCases: 7).Should().HaveCount(7);
    }
}
