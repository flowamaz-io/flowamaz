using FluentAssertions;
using Flowamaz.Application.Workflow.Dna;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Dna;

public sealed class WorkflowDnaServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private const string SimpleYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: test
          name: "Invoice Approval"
        spec:
          trigger:
            type: webhook
          nodes:
            - id: start
              type: trigger
              label: "Start"
              config: {}
            - id: approve
              type: human-gate
              label: "Approval"
              config: {}
            - id: notify
              type: action
              label: "Notify"
              config:
                url: "https://api.example.com"
                method: POST
            - id: end
              type: end
              label: "End"
              config: {}
          edges:
            - id: e1
              from: start
              to: approve
            - id: e2
              from: approve
              to: notify
            - id: e3
              from: notify
              to: end
        """;

    private WorkflowDnaService BuildService(
        WorkflowDefinition? definition = null,
        List<WorkflowMetric>? metrics = null)
    {
        var def = definition ?? new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            WorkspaceId = WorkspaceId,
            Name = "Invoice Approval",
            Slug = "invoice-approval",
            YamlContent = SimpleYaml,
            TriggerType = WorkflowTriggerType.Webhook,
            Status = WorkflowStatus.Published,
            CurrentVersion = "1.0.0",
        };

        var defRepo = new Mock<IWorkflowDefinitionRepository>();
        defRepo.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(def);
        defRepo.Setup(r => r.GetForWorkspaceAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowDefinition> { def });

        var metricRepo = new Mock<IWorkflowMetricRepository>();
        metricRepo.Setup(r => r.GetForDefinitionSinceAsync(It.IsAny<Guid>(), WorkspaceId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics ?? new List<WorkflowMetric>());

        return new WorkflowDnaService(defRepo.Object, metricRepo.Object, NullLogger<WorkflowDnaService>.Instance);
    }

    [Fact]
    public async Task ComputeDna_Identifies_NodeTypes_Correctly()
    {
        var svc = BuildService();
        var wfId = Guid.NewGuid();

        var dna = await svc.ComputeDnaAsync(wfId, WorkspaceId);

        dna.HasHumanGates.Should().BeTrue();
        dna.HasAiNodes.Should().BeFalse();
        dna.NodeTypes.Should().Contain("human-gate");
        dna.NodeTypes.Should().Contain("action");
    }

    [Fact]
    public async Task ComputeDna_Hash_Is_Deterministic()
    {
        var svc = BuildService();
        var wfId = Guid.NewGuid();

        var dna1 = await svc.ComputeDnaAsync(wfId, WorkspaceId);
        var dna2 = await svc.ComputeDnaAsync(wfId, WorkspaceId);

        dna1.DnaHash.Should().Be(dna2.DnaHash);
    }

    [Fact]
    public async Task FindSimilar_Same_Trigger_And_Gates_Gets_High_Score()
    {
        var wf1 = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            WorkspaceId = WorkspaceId,
            Name = "Invoice Approval",
            Slug = "invoice-approval",
            YamlContent = SimpleYaml,
            TriggerType = WorkflowTriggerType.Webhook,
            Status = WorkflowStatus.Published,
            CurrentVersion = "1.0.0",
        };
        var wf2 = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            WorkspaceId = WorkspaceId,
            Name = "Purchase Order Approval",
            Slug = "purchase-approval",
            YamlContent = SimpleYaml,  // same structure
            TriggerType = WorkflowTriggerType.Webhook,
            Status = WorkflowStatus.Published,
            CurrentVersion = "1.0.0",
        };

        var defRepo = new Mock<IWorkflowDefinitionRepository>();
        defRepo.Setup(r => r.GetByIdForWorkspaceAsync(wf1.Id, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wf1);
        defRepo.Setup(r => r.GetForWorkspaceAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowDefinition> { wf1, wf2 });
        var metricRepo = new Mock<IWorkflowMetricRepository>();
        metricRepo.Setup(r => r.GetForDefinitionSinceAsync(It.IsAny<Guid>(), WorkspaceId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowMetric>());

        var svc = new WorkflowDnaService(defRepo.Object, metricRepo.Object, NullLogger<WorkflowDnaService>.Instance);
        var dna = await svc.ComputeDnaAsync(wf1.Id, WorkspaceId);
        var similar = await svc.FindSimilarAsync(WorkspaceId, dna, 10);

        similar.Should().HaveCount(1);
        similar[0].SimilarityScore.Should().BeGreaterThan(50);
    }

    [Fact]
    public async Task FindSimilar_Completely_Different_Workflow_Gets_Low_Score()
    {
        var differentYaml = """
            spec:
              nodes:
                - id: n1
                  type: ai
                - id: n2
                  type: parallel
              edges:
                - id: e1
                  from: n1
                  to: n2
            """;
        var wf1 = new WorkflowDefinition
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "A",
            Slug = "a", YamlContent = SimpleYaml, TriggerType = WorkflowTriggerType.Webhook,
            Status = WorkflowStatus.Published, CurrentVersion = "1",
        };
        var wf2 = new WorkflowDefinition
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "B",
            Slug = "b", YamlContent = differentYaml, TriggerType = WorkflowTriggerType.Schedule,
            Status = WorkflowStatus.Published, CurrentVersion = "1",
        };

        var defRepo = new Mock<IWorkflowDefinitionRepository>();
        defRepo.Setup(r => r.GetByIdForWorkspaceAsync(wf1.Id, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wf1);
        defRepo.Setup(r => r.GetForWorkspaceAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowDefinition> { wf1, wf2 });
        var metricRepo = new Mock<IWorkflowMetricRepository>();
        metricRepo.Setup(r => r.GetForDefinitionSinceAsync(It.IsAny<Guid>(), WorkspaceId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowMetric>());

        var svc = new WorkflowDnaService(defRepo.Object, metricRepo.Object, NullLogger<WorkflowDnaService>.Instance);
        var dna = await svc.ComputeDnaAsync(wf1.Id, WorkspaceId);
        var similar = await svc.FindSimilarAsync(WorkspaceId, dna, 10);

        similar[0].SimilarityScore.Should().BeLessThan(20);
    }

    [Fact]
    public async Task SuggestClone_Returns_Null_For_Short_Names()
    {
        var svc = BuildService();

        var result = await svc.SuggestCloneAsync(WorkspaceId, "ab");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SuggestClone_Returns_Suggestion_When_Name_Matches()
    {
        var svc = BuildService();

        var result = await svc.SuggestCloneAsync(WorkspaceId, "Invoice Approval");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Invoice Approval");
        result.SimilarityScore.Should().Be(100);
    }
}
