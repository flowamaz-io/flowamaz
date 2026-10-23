using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

public sealed class CopilotPatternMatcherTests
{
    private static ICopilotPatternMatcher Create() => new CopilotPatternMatcher(NullLogger<CopilotPatternMatcher>.Instance);

    [Theory]
    [InlineData("connect validate-employee to amount-router", "connect-nodes")]
    [InlineData("Connect start to end", "connect-nodes")]
    public void ConnectNodes_MatchesAndProducesEdge(string command, string expectedPattern)
    {
        var result = Create().TryMatch(command);
        result.Should().NotBeNull();
        result!.PatternName.Should().Be(expectedPattern);
        result.YamlPatch.Should().Contain("edges:");
        result.YamlPatch.Should().Contain("from:");
        result.YamlPatch.Should().Contain("to:");
    }

    [Theory]
    [InlineData("Add manager approval after validate-employee", "add-human-gate")]
    [InlineData("add approval gate after send-request", "add-human-gate")]
    [InlineData("add review after data-check", "add-human-gate")]
    public void AddHumanGate_MatchesAndProducesGateNode(string command, string expectedPattern)
    {
        var result = Create().TryMatch(command);
        result.Should().NotBeNull();
        result!.PatternName.Should().Be(expectedPattern);
        result.YamlPatch.Should().Contain("human-gate");
    }

    [Theory]
    [InlineData("add error handler", "add-error-handler")]
    [InlineData("Add a failure handler for validate-step", "add-error-handler")]
    public void AddErrorHandler_MatchesAndProducesTryCatch(string command, string expectedPattern)
    {
        var result = Create().TryMatch(command);
        result.Should().NotBeNull();
        result!.PatternName.Should().Be(expectedPattern);
        result.YamlPatch.Should().Contain("try-catch");
    }

    [Theory]
    [InlineData("delete node old-step", "delete-node")]
    [InlineData("remove validate-request", "delete-node")]
    public void DeleteNode_MatchesWithNodeId(string command, string expectedPattern)
    {
        var result = Create().TryMatch(command);
        result.Should().NotBeNull();
        result!.PatternName.Should().Be(expectedPattern);
        result.YamlPatch.Should().Contain("DELETE");
    }

    [Fact]
    public void RenameNode_MatchesAndProducesLabel()
    {
        var result = Create().TryMatch("rename validate-step to Check Employee Data");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("rename-node");
        result.YamlPatch.Should().Contain("label");
    }

    [Fact]
    public void AddAnnotation_MatchesAndProducesAnnotationNode()
    {
        var result = Create().TryMatch("add annotation \"Review this step\"");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-annotation");
        result.YamlPatch.Should().Contain("annotation");
    }

    [Fact]
    public void SetVariable_MatchesAndProducesVariableSpec()
    {
        var result = Create().TryMatch("add variable amount as number");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("set-variable");
        result.YamlPatch.Should().Contain("amount");
        result.YamlPatch.Should().Contain("number");
    }

    [Fact]
    public void AddParallel_MatchesAndProducesParallelNode()
    {
        var result = Create().TryMatch("run steps in parallel");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-parallel");
        result.YamlPatch.Should().Contain("parallel");
    }

    [Fact]
    public void AddLoop_MatchesAndProducesForeachNode()
    {
        var result = Create().TryMatch("loop over items");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-loop");
        result.YamlPatch.Should().Contain("foreach");
    }

    [Fact]
    public void AddAiNode_MatchesAndProducesAiNode()
    {
        var result = Create().TryMatch("add an ai step called Summarize Report");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-ai-node");
        result.YamlPatch.Should().Contain("ai");
    }

    [Fact]
    public void AddActionNode_MatchesAndProducesActionNode()
    {
        var result = Create().TryMatch("add action Send Notification Email");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-action-node");
        result.YamlPatch.Should().Contain("action");
    }

    [Fact]
    public void SetCompensation_MatchesAndProducesStrategy()
    {
        var result = Create().TryMatch("set compensation on payment-step backward");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("set-compensation");
        result.YamlPatch.Should().Contain("backward");
    }

    [Fact]
    public void AddCondition_MatchesAndProducesRouterNode()
    {
        var result = Create().TryMatch("add condition amount > 1000");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-condition");
        result.YamlPatch.Should().Contain("router");
    }

    [Fact]
    public void AddWait_MatchesWithCorrectDuration()
    {
        var result = Create().TryMatch("add wait for 2 hours");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-wait");
        result.YamlPatch.Should().Contain("7200");
    }

    [Fact]
    public void AddTrigger_MatchesAndSetsTriggerType()
    {
        var result = Create().TryMatch("trigger on webhook");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-trigger");
        result.YamlPatch.Should().Contain("webhook");
    }

    [Fact]
    public void AddSubWorkflow_MatchesAndProducesSubWorkflowNode()
    {
        var result = Create().TryMatch("add sub-workflow Employee Onboarding");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-sub-workflow");
        result.YamlPatch.Should().Contain("sub-workflow");
    }

    [Fact]
    public void AddSensitiveVariable_MatchesAndSetsSensitiveTrue()
    {
        var result = Create().TryMatch("add sensitive variable api_secret");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("add-sensitive-variable");
        result.YamlPatch.Should().Contain("sensitive: true");
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("what is the weather today")]
    [InlineData("list all nodes")]
    [InlineData("")]
    public void UnknownCommand_ReturnsNull(string command)
    {
        var result = Create().TryMatch(command);
        result.Should().BeNull();
    }

    // ── Connector pattern tests (prompt 04-07) ───────────────────────────

    [Fact]
    public void SendSlackMessage_MatchesAndContainsSlackConnectorId()
    {
        var result = Create().TryMatch("Send Slack message to #finance");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("send-slack-message");
        result.YamlPatch.Should().Contain("connector_id: slack");
        result.YamlPatch.Should().Contain("#finance");
    }

    [Fact]
    public void ScheduleCron_MatchesAndContainsScheduleCronConnectorId()
    {
        var result = Create().TryMatch("Schedule every Monday 9am");
        result.Should().NotBeNull();
        result!.PatternName.Should().Be("schedule-cron");
        result.YamlPatch.Should().Contain("connector_id: schedule-cron");
    }

    // ── B9: gate timeout (the Co-pilot input's own placeholder example) ──────────

    private const string GateWorkflowYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: purchase, name: Purchase }
        spec:
          trigger: { type: manual }
          nodes:
            - id: purchase-requested
              type: trigger
              label: Purchase requested
            - id: manager-approval
              type: human-gate
              label: Manager approval
              config:
                assignee: manager@acme.test
                on_timeout: escalate
            - id: notify
              type: action
              label: Notify
          edges:
            - { id: e1, from: purchase-requested, to: manager-approval }
            - { id: e2, from: manager-approval, to: notify }
        """;

    [Theory]
    [InlineData("Add a 24h timeout to manager-approval", 24)]
    [InlineData("add a 24 hour timeout to manager-approval", 24)]
    [InlineData("add 48-hour timeout on the manager-approval node", 48)]
    [InlineData("set timeout of 2 days on manager-approval", 48)]
    [InlineData("Set a 3d timeout for manager-approval.", 72)]
    [InlineData("set the timeout on manager-approval to 12 hours", 12)]
    [InlineData("add a 1 day timeout to Manager-Approval", 24)]
    public void SetGateTimeout_matches_and_patches_the_complete_gate_node(string command, int expectedHours)
    {
        var result = Create().TryMatch(command, GateWorkflowYaml);

        result.Should().NotBeNull();
        result!.PatternName.Should().Be("set-gate-timeout");

        var patch = new YamlDotNet.Serialization.DeserializerBuilder().Build()
            .Deserialize<Dictionary<object, object>>(result.YamlPatch);
        var nodes = (List<object>)((Dictionary<object, object>)patch["spec"])["nodes"];
        var node = (Dictionary<object, object>)nodes.Should().ContainSingle().Subject;
        node["id"].Should().Be("manager-approval");
        node["type"].Should().Be("human-gate");
        node["label"].Should().Be("Manager approval");
        var config = (Dictionary<object, object>)node["config"];
        config["timeout_hours"].Should().Be(expectedHours.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Existing config is kept: the editor replaces the whole node by id.
        config["assignee"].Should().Be("manager@acme.test");
        config["on_timeout"].Should().Be("escalate");
    }

    [Fact]
    public void SetGateTimeout_matches_a_node_by_its_label()
    {
        var result = Create().TryMatch("add a 24h timeout to manager_approval", GateWorkflowYaml);
        result.Should().NotBeNull();
        result!.YamlPatch.Should().Contain("id: manager-approval").And.Contain("timeout_hours: 24");
    }

    [Fact]
    public void SetGateTimeout_falls_through_when_the_node_is_unknown_or_not_a_gate_or_yaml_missing()
    {
        Create().TryMatch("add a 24h timeout to no-such-node", GateWorkflowYaml).Should().BeNull();
        Create().TryMatch("add a 24h timeout to notify", GateWorkflowYaml).Should().BeNull();
        Create().TryMatch("add a 24h timeout to manager-approval", null).Should().BeNull();
    }
}
