using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Application.Workflow.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Flowamaz.Tests.Unit.Workflow;

public class GeneratedYamlRepairTests(ITestOutputHelper output)
{
    private static readonly WorkflowValidator Validator = new(NullLogger<WorkflowValidator>.Instance);

    // The kind of near-miss a model produces: invalid trigger type, ${} references, extra node keys,
    // "decision"/"approval" types, ids with underscores, edges without ids, labels instead of via, and
    // numbers the schema expects as integers.
    private const string NearMiss = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: Expense_Claim
          name: "Expense Claim"
          description: "Claims over 1000 need Finance"
          version: "1.0"
          author: ai
        spec:
          trigger:
            type: event
          sla_threshold_ms: 172800000
          variables:
            amount:
              type: number
              required: true
          nodes:
            - id: start
              type: start
              label: "Claim submitted"
            - id: manager_review
              type: approval
              label: "Manager reviews ${amount}"
              assignee: manager
              timeout:
                seconds: 86400
            - id: over_limit
              type: decision
              label: "Over 1000?"
              config:
                branches:
                  - { label: "yes", condition: "${amount} > 1000" }
                  - { label: "no", condition: "${amount} <= 1000" }
            - id: finance
              type: approval
              label: "Finance approves"
            - id: pay
              type: task
              label: "Pay employee"
              description: "Bank transfer"
            - id: done
              type: finish
              label: "Done"
          edges:
            - { from: start, to: manager_review }
            - { from: manager_review, to: over_limit }
            - { from: over_limit, to: finance, label: "yes" }
            - { from: over_limit, to: pay, label: "no" }
            - { from: finance, to: pay }
            - { from: pay, to: done }
        """;

    [Fact]
    public async Task Near_miss_from_a_model_validates_after_repair()
    {
        (await Validator.ValidateAsync(NearMiss, Guid.NewGuid())).IsValid.Should().BeFalse("the fixture must start invalid");

        var repaired = GeneratedYamlRepair.Repair(NearMiss);
        output.WriteLine(repaired);
        var result = await Validator.ValidateAsync(repaired, Guid.NewGuid());
        foreach (var e in result.Errors) output.WriteLine($"{e.Code} {e.NodeId} {e.Message}");

        result.Errors.Should().BeEmpty();
        repaired.Should().NotContain("${").And.Contain("{{ amount }} > 1000")
            .And.Contain("type: webhook").And.Contain("type: human-gate").And.Contain("type: router")
            .And.Contain("from: over-limit").And.Contain("via: yes").And.NotContain("author:");
    }

    [Fact]
    public void Extra_node_keys_move_into_config()
    {
        var repaired = GeneratedYamlRepair.Repair(NearMiss);
        repaired.Should().Contain("assignee: manager").And.Contain("description: Bank transfer");
    }

    [Fact]
    public void Valid_yaml_is_returned_byte_for_byte()
    {
        const string yaml = "apiVersion: flowamaz/v1\nkind: Workflow\n# a comment\nmetadata: { id: a, name: A }\nspec:\n  trigger: { type: manual }\n  nodes: []\n  edges: []\n";
        GeneratedYamlRepair.Repair(yaml).Should().BeSameAs(yaml);
    }

    [Fact]
    public void Unreadable_input_is_returned_unchanged()
    {
        GeneratedYamlRepair.Repair("::: not yaml [").Should().Be("::: not yaml [");
    }
}
