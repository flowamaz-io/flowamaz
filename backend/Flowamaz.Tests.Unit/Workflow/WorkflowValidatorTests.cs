using FluentAssertions;
using Flowamaz.Application.Workflow.Validation;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Workflow;

public sealed class WorkflowValidatorTests
{
    private static IWorkflowValidator Create() => new WorkflowValidator(NullLogger<WorkflowValidator>.Instance);

    private const string ValidYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: purchase-approval
          name: "Purchase Approval"
          description: "Approves purchase requests"
          version: "1.0.0"
        spec:
          trigger:
            type: webhook
          variables:
            amount:
              type: number
              required: true
              sensitive: false
          nodes:
            - id: start
              type: trigger
              label: "Start"
            - id: approve
              type: action
              label: "Approve"
            - id: done
              type: end
              label: "Done"
          edges:
            - id: e1
              from: start
              to: approve
            - id: e2
              from: approve
              to: done
        """;

    [Fact]
    public async Task ValidYaml_Returns_IsValid_True()
    {
        var validator = Create();
        var result = await validator.ValidateAsync(ValidYaml);
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingTriggerNode_Returns_GRF001_Error()
    {
        var yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              id: no-trigger
              name: "No Trigger"
            spec:
              trigger:
                type: webhook
              nodes:
                - id: approve
                  type: action
                  label: "Approve"
                - id: done
                  type: end
                  label: "Done"
              edges:
                - id: e1
                  from: approve
                  to: done
            """;
        var validator = Create();
        var result = await validator.ValidateAsync(yaml);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "GRF-001");
    }

    [Fact]
    public async Task EdgeToUnknownNode_Returns_GRF002_Error()
    {
        var yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              id: bad-edge
              name: "Bad Edge"
            spec:
              trigger:
                type: webhook
              nodes:
                - id: start
                  type: trigger
                  label: "Start"
                - id: done
                  type: end
                  label: "Done"
              edges:
                - id: e1
                  from: start
                  to: nonexistent
            """;
        var validator = Create();
        var result = await validator.ValidateAsync(yaml);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "GRF-002");
    }

    [Fact]
    public async Task OrphanedNode_Returns_GRF003_Error()
    {
        var yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              id: orphan
              name: "Orphan Test"
            spec:
              trigger:
                type: webhook
              nodes:
                - id: start
                  type: trigger
                  label: "Start"
                - id: done
                  type: end
                  label: "Done"
                - id: orphan
                  type: action
                  label: "Orphan"
              edges:
                - id: e1
                  from: start
                  to: done
            """;
        var validator = Create();
        var result = await validator.ValidateAsync(yaml);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "GRF-003" && e.NodeId == "orphan");
    }

    [Fact]
    public async Task HardcodedSecret_Sk_Prefix_Returns_SEC001_Error()
    {
        var yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              id: secret-test
              name: "Secret Test"
              description: "Test with hardcoded secret"
            spec:
              trigger:
                type: webhook
                config:
                  api_key: sk-abcdefghijklmnopqrstuvwxyz1234567890
              nodes:
                - id: start
                  type: trigger
                  label: "Start"
                - id: done
                  type: end
                  label: "Done"
              edges:
                - id: e1
                  from: start
                  to: done
            """;
        var validator = Create();
        var result = await validator.ValidateAsync(yaml);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "SEC-001");
    }

    [Fact]
    public async Task GateWithNoTimeout_Returns_BPR001_Warning()
    {
        var yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              id: gate-no-timeout
              name: "Gate No Timeout"
              description: "Test"
            spec:
              trigger:
                type: webhook
              nodes:
                - id: start
                  type: trigger
                  label: "Start"
                - id: gate
                  type: human-gate
                  label: "Manager Approval"
                - id: done
                  type: end
                  label: "Done"
              edges:
                - id: e1
                  from: start
                  to: gate
                - id: e2
                  from: gate
                  to: done
            """;
        var validator = Create();
        var result = await validator.ValidateAsync(yaml);
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Code == "BPR-001" && w.NodeId == "gate");
    }

    [Fact]
    public async Task UnknownVariableReference_Returns_EXP001_Warning()
    {
        var yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              id: expr-test
              name: "Expr Test"
              description: "Test"
            spec:
              trigger:
                type: webhook
              nodes:
                - id: start
                  type: trigger
                  label: "Start"
                  config:
                    message: "Hello {{ variables.undeclared_var }}"
                - id: done
                  type: end
                  label: "Done"
              edges:
                - id: e1
                  from: start
                  to: done
            """;
        var validator = Create();
        var result = await validator.ValidateAsync(yaml);
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Code == "EXP-001");
    }

    [Fact]
    public async Task ValidYaml_WithHighRetry_Returns_BPR002_Warning()
    {
        var yaml = """
            apiVersion: flowamaz/v1
            kind: Workflow
            metadata:
              id: high-retry
              name: "High Retry"
              description: "Test"
            spec:
              trigger:
                type: webhook
              nodes:
                - id: start
                  type: trigger
                  label: "Start"
                - id: action
                  type: action
                  label: "Action"
                  retry:
                    max_attempts: 15
                    backoff_seconds: 5
                    backoff_multiplier: 2.0
                - id: done
                  type: end
                  label: "Done"
              edges:
                - id: e1
                  from: start
                  to: action
                - id: e2
                  from: action
                  to: done
            """;
        var validator = Create();
        var result = await validator.ValidateAsync(yaml);
        result.Warnings.Should().Contain(w => w.Code == "BPR-002" && w.NodeId == "action");
    }
}
