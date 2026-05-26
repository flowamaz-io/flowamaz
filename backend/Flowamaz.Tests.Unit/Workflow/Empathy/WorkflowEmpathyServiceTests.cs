using FluentAssertions;
using Flowamaz.Application.Workflow.Empathy;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Empathy;

public sealed class WorkflowEmpathyServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    // Workflow with no notification nodes
    private const string YamlNoNotifications = """
        workflow:
          id: test-no-notify
          version: v1
          name: "Silent Workflow"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          - id: gate
            type: HumanGate
            label: "Approval Gate"
          - id: end
            type: End
            label: "End"
        edges:
          - id: e1
            from: start
            to: gate
          - id: e2
            from: gate
            to: end
        """;

    // Workflow with HumanGate and no status-update nodes
    private const string YamlGateNoUpdates = """
        workflow:
          id: test-gate-no-updates
          version: v1
          name: "Gate No Updates"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          - id: gate
            type: HumanGate
            label: "Review Gate"
          - id: process
            type: Action
            label: "Run process"
            config:
              connector_id: "postgres"
              operation_id: "execute"
          - id: end
            type: End
            label: "End"
        edges:
          - id: e1
            from: start
            to: gate
          - id: e2
            from: gate
            to: process
          - id: e3
            from: process
            to: end
        """;

    // Workflow with 7 email nodes
    private const string YamlManyEmails = """
        workflow:
          id: test-many-emails
          version: v1
          name: "Email Heavy"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          - id: email1
            type: Action
            label: "Email 1"
            config:
              connector_id: "email-smtp"
          - id: email2
            type: Action
            label: "Email 2"
            config:
              connector_id: "email-smtp"
          - id: email3
            type: Action
            label: "Email 3"
            config:
              connector_id: "email-smtp"
          - id: email4
            type: Action
            label: "Email 4"
            config:
              connector_id: "email-smtp"
          - id: email5
            type: Action
            label: "Email 5"
            config:
              connector_id: "email-smtp"
          - id: email6
            type: Action
            label: "Email 6"
            config:
              connector_id: "email-smtp"
          - id: email7
            type: Action
            label: "Email 7"
            config:
              connector_id: "email-smtp"
          - id: end
            type: End
            label: "End"
        edges:
          - id: e1
            from: start
            to: email1
          - id: e2
            from: email1
            to: email2
          - id: e3
            from: email2
            to: email3
          - id: e4
            from: email3
            to: email4
          - id: e5
            from: email4
            to: email5
          - id: e6
            from: email5
            to: email6
          - id: e7
            from: email6
            to: email7
          - id: e8
            from: email7
            to: end
        """;

    // Well-designed workflow: 1 email, 1 Slack update, gate with short timeout
    private const string YamlWellDesigned = """
        workflow:
          id: test-well-designed
          version: v1
          name: "Well Designed"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          - id: notify-received
            type: Action
            label: "Notify received"
            config:
              connector_id: "email-smtp"
          - id: gate
            type: HumanGate
            label: "Approval"
            timeout:
              timeoutSeconds: 86400
          - id: notify-done
            type: Action
            label: "Notify completed"
            config:
              connector_id: "slack"
          - id: end
            type: End
            label: "End"
        edges:
          - id: e1
            from: start
            to: notify-received
          - id: e2
            from: notify-received
            to: gate
          - id: e3
            from: gate
            to: notify-done
          - id: e4
            from: notify-done
            to: end
        """;

    private WorkflowEmpathyService BuildService(
        string yaml,
        List<WorkflowMetric>? metrics = null)
    {
        var def = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            WorkspaceId = WorkspaceId,
            Name = "Test Workflow",
            Slug = "test-workflow",
            YamlContent = yaml,
            TriggerType = WorkflowTriggerType.Manual,
            Status = WorkflowStatus.Draft,
            CurrentVersion = "draft",
        };

        var defRepo = new Mock<IWorkflowDefinitionRepository>();
        defRepo
            .Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(def);

        var metricRepo = new Mock<IWorkflowMetricRepository>();
        metricRepo
            .Setup(r => r.GetForDefinitionSinceAsync(It.IsAny<Guid>(), WorkspaceId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics ?? new List<WorkflowMetric>());

        return new WorkflowEmpathyService(
            defRepo.Object,
            metricRepo.Object,
            new SfgParser(),
            NullLogger<WorkflowEmpathyService>.Instance);
    }

    [Fact]
    public async Task NoEmailOrSlack_Returns_NoOutcomeNotificationIssue()
    {
        var svc = BuildService(YamlNoNotifications);

        var result = await svc.AnalyseAsync(Guid.NewGuid(), WorkspaceId);

        result.Issues.Should().Contain(i => i.Type == Core.Enums.EmpathyIssueType.NoOutcomeNotification);
        result.EmailsSentToRequester.Should().Be(0);
    }

    [Fact]
    public async Task HumanGateWithNoStatusUpdates_Returns_VisibilityGapIssue()
    {
        var svc = BuildService(YamlGateNoUpdates);

        var result = await svc.AnalyseAsync(Guid.NewGuid(), WorkspaceId);

        result.Issues.Should().Contain(i => i.Type == Core.Enums.EmpathyIssueType.VisibilityGap);
        result.WaitPeriodCount.Should().Be(1);
    }

    [Fact]
    public async Task SevenEmailNodes_Returns_MultipleEmailsIssue_And_ScoreBelow100()
    {
        var svc = BuildService(YamlManyEmails);

        var result = await svc.AnalyseAsync(Guid.NewGuid(), WorkspaceId);

        result.Issues.Should().Contain(i => i.Type == Core.Enums.EmpathyIssueType.MultipleEmails);
        result.EmailsSentToRequester.Should().Be(7);
        result.Score.Should().BeLessThan(100);
    }

    [Fact]
    public async Task WellDesignedWorkflow_Returns_NoIssues_And_HighScore()
    {
        var svc = BuildService(YamlWellDesigned);

        var result = await svc.AnalyseAsync(Guid.NewGuid(), WorkspaceId);

        result.Issues.Should().BeEmpty();
        result.Score.Should().Be(100);
    }
}
