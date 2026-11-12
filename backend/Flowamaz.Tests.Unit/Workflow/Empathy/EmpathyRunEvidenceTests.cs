using FluentAssertions;
using Flowamaz.Application.Workflow.Empathy;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Empathy;

/// <summary>The Empathy View measured from real runs (§8.2): messages, silences, waiting, outcome announced.</summary>
public sealed class EmpathyRunEvidenceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private const string Yaml = """
        workflow:
          id: onboarding
          version: v1
          name: "Onboarding"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          - id: notify
            type: Action
            label: "Email the new hire"
            config:
              connector: "email-smtp"
              operation: "send-email"
          - id: it
            type: HumanGate
            label: "IT approval"
          - id: done
            type: Action
            label: "Tell the new hire"
            config:
              connector_id: "slack"
              operation: "send-message"
          - id: end
            type: End
            label: "End"
        edges:
          - { id: e1, from: start, to: notify }
          - { id: e2, from: notify, to: it }
          - { id: e3, from: it, to: done }
          - { id: e4, from: done, to: end }
        """;

    private static readonly HashSet<string> Notify = ["notify", "done"];

    private static EmpathyRunTimeline Run(double hours, double[] messagesAt, (double Opened, double? Decided)[]? gates = null) =>
        new(Guid.NewGuid(), T0, T0.AddHours(hours),
            messagesAt.Select((h, i) => (i % 2 == 0 ? "notify" : "done", T0.AddHours(h))).ToList(),
            (gates ?? []).Select(g => ("it", T0.AddHours(g.Opened), g.Decided is { } d ? T0.AddHours(d) : (DateTime?)null)).ToList());

    [Fact]
    public void Measures_messages_silence_waiting_and_outcome()
    {
        var e = WorkflowEmpathyService.Measure(
        [
            Run(72, [1, 71], [(2, 70)]),   // quiet 70 h; waited 68 of 72 h; told the outcome
            Run(48, [1], [(2, 40)]),       // quiet 47 h at the end; nothing after the decision
            Run(24, [1, 2, 3, 23], [(4, 22)]),
        ], Notify)!;

        e.RunsAnalysed.Should().Be(3);
        e.AvgDaysToOutcome.Should().Be(2);
        e.AvgMessagesPerRun.Should().BeApproximately(2.3, 0.05);
        e.MedianLongestSilenceHours.Should().Be(47);
        e.PercentRunsOutcomeNotified.Should().Be(67);
        e.SlowestGateNodeId.Should().Be("it");
        e.SlowestGateAvgHours.Should().BeApproximately(41.3, 0.05);
        e.PercentTimeWaitingOnApprovals.Should().BeInRange(70, 85);
    }

    [Fact]
    public void No_finished_runs_means_no_evidence() =>
        WorkflowEmpathyService.Measure([], Notify).Should().BeNull();

    [Fact]
    public void Findings_quote_the_numbers_and_say_what_to_change()
    {
        var graph = new SfgParser().Parse(Yaml);
        var issues = WorkflowEmpathyService.RunIssues(new EmpathyRunEvidence(12, 3, 7, 80, 85, 20, "it", 60), graph);

        issues.Should().Contain(i => i.Type == EmpathyIssueType.MultipleEmails && i.Description.Contains("7 messages per run over 3 days"));
        issues.Should().Contain(i => i.Type == EmpathyIssueType.VisibilityGap && i.Severity == "High" && i.Description.Contains("80 hours"));
        issues.Should().Contain(i => i.Type == EmpathyIssueType.NoOutcomeNotification && i.Description.Contains("Only 20%"));
        issues.Should().Contain(i => i.Type == EmpathyIssueType.LongWait && i.NodeId == "it" && i.Description.Contains("'IT approval' took 60 hours"));
        issues.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Suggestion));
    }

    [Fact]
    public void A_considerate_workflow_gets_no_run_findings()
    {
        var graph = new SfgParser().Parse(Yaml);
        WorkflowEmpathyService.RunIssues(new EmpathyRunEvidence(5, 1, 3, 6, 30, 100, "it", 4), graph).Should().BeEmpty();
    }

    [Fact]
    public async Task The_analysis_uses_real_runs_when_there_are_some()
    {
        var id = Guid.NewGuid();
        var defs = new Mock<IWorkflowDefinitionRepository>();
        defs.Setup(r => r.GetByIdForWorkspaceAsync(id, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition { Id = id, WorkspaceId = WorkspaceId, Name = "Onboarding", YamlContent = Yaml });
        var metrics = new Mock<IWorkflowMetricRepository>();
        metrics.Setup(r => r.GetForDefinitionSinceAsync(id, WorkspaceId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var events = new Mock<IWorkflowEventRepository>();
        events.Setup(r => r.GetRunTimelinesAsync(WorkspaceId, id, It.IsAny<DateTime>(), 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Run(96, [1], [(2, 94)])]);
        var service = new WorkflowEmpathyService(defs.Object, metrics.Object, new SfgParser(), NullLogger<WorkflowEmpathyService>.Instance, events.Object);

        var result = await service.AnalyseAsync(id, WorkspaceId);

        result.Runs.Should().NotBeNull();
        result.AvgDaysToOutcome.Should().Be(4);
        result.VisibilityGapHours.Should().Be(95);
        result.EmailsSentToRequester.Should().Be(1, "the design check now also reads the 'connector' key");
        result.Issues.Should().Contain(i => i.Label == "Outcome rarely announced");
    }
}
