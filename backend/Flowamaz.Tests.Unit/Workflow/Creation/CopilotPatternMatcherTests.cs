using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

public sealed class CopilotPatternMatcherTests
{
    private static ICopilotPatternMatcher Create() => new CopilotPatternMatcher(NullLogger<CopilotPatternMatcher>.Instance);

    [Theory]
    [InlineData("Add 48h timeout to manager-gate", "add-timeout", "172800")]
    [InlineData("add 30 minute timeout on validate-step", "add-timeout", "1800")]
    [InlineData("add timeout of 60 seconds to send-email", "add-timeout", "60")]
    public void AddTimeout_MatchesWithCorrectSeconds(string command, string expectedPattern, string expectedSeconds)
    {
        var result = Create().TryMatch(command);
        result.Should().NotBeNull();
        result!.PatternName.Should().Be(expectedPattern);
        result.YamlPatch.Should().Contain(expectedSeconds);
    }

    [Theory]
    [InlineData("Retry the SAP step 3 times", "add-retry", "3")]
    [InlineData("retry 5 times for send-email", "add-retry", "5")]
    public void AddRetry_MatchesWithCorrectAttempts(string command, string expectedPattern, string expectedAttempts)
    {
        var result = Create().TryMatch(command);
        result.Should().NotBeNull();
        result!.PatternName.Should().Be(expectedPattern);
        result.YamlPatch.Should().Contain(expectedAttempts);
    }

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
    [InlineData("Set SLA to 48 hours", "set-sla", "172800000")]
    [InlineData("sla 2 days", "set-sla", "172800000")]
    public void SetSla_MatchesWithCorrectMs(string command, string expectedPattern, string expectedMs)
    {
        var result = Create().TryMatch(command);
        result.Should().NotBeNull();
        result!.PatternName.Should().Be(expectedPattern);
        result.YamlPatch.Should().Contain(expectedMs);
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
}
