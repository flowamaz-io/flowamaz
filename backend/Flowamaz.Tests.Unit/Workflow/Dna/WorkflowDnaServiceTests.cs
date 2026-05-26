using FluentAssertions;
using Flowamaz.Application.Workflow.Dna;
using Flowamaz.Core.Entities.Analytics;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Dna;

public sealed class WorkflowDnaServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    // Top-level nodes/edges (SfgParser-compatible). PascalCase types match NodeType enum.
    private const string SimpleYaml = """
        workflow:
          id: test
          version: v1
          name: "Invoice Approval"
        nodes:
          - id: start
            type: Trigger
            label: "Start"
          - id: approve
            type: HumanGate
            label: "Approval"
          - id: notify
            type: Action
            label: "Notify"
            config:
              url: "https://api.example.com"
              method: POST
          - id: end
            type: End
            label: "End"
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

        return new WorkflowDnaService(defRepo.Object, metricRepo.Object, new SfgParser(), NullLogger<WorkflowDnaService>.Instance);
    }

    [Fact]
    public async Task ComputeDna_Identifies_NodeTypes_Correctly()
    {
        var svc = BuildService();
        var wfId = Guid.NewGuid();

        var dna = await svc.ComputeDnaAsync(wfId, WorkspaceId);

        dna.HasHumanGates.Should().BeTrue();
        dna.HasAiNodes.Should().BeFalse();
        dna.NodeTypes.Should().Contain("humangate");
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
            YamlContent = SimpleYaml,
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

        var svc = new WorkflowDnaService(defRepo.Object, metricRepo.Object, new SfgParser(), NullLogger<WorkflowDnaService>.Instance);
        var dna = await svc.ComputeDnaAsync(wf1.Id, WorkspaceId);
        var similar = await svc.FindSimilarAsync(WorkspaceId, dna, 10);

        similar.Should().HaveCount(1);
        similar[0].SimilarityScore.Should().BeGreaterThan(50);
    }

    [Fact]
    public async Task FindSimilar_Completely_Different_Workflow_Gets_Low_Score()
    {
        const string differentYaml = """
            workflow:
              id: ai-batch
              version: v1
              name: "AI Batch Processing"
            nodes:
              - id: start
                type: Trigger
                label: "Start"
              - id: n1
                type: Ai
                label: "AI Step"
              - id: n2
                type: Parallel
                label: "Parallel Step"
              - id: done
                type: End
                label: "Done"
            edges:
              - id: e1
                from: start
                to: n1
              - id: e2
                from: n1
                to: n2
              - id: e3
                from: n2
                to: done
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

        var svc = new WorkflowDnaService(defRepo.Object, metricRepo.Object, new SfgParser(), NullLogger<WorkflowDnaService>.Instance);
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

    // ── New tests for fix-03-01 ──────────────────────────────────────────────

    [Fact]
    public async Task ComputeDnaAsync_ValidYaml_UsesGraphNotRegex()
    {
        // UPPERCASE type names — Enum.TryParse(ignoreCase:true) handles them;
        // the old regex would have captured "HUMANGATE" and never matched "human-gate".
        const string mixedCaseYaml = """
            workflow:
              id: mixed
              version: v1
              name: "Mixed Case"
            nodes:
              - id: start
                type: TRIGGER
                label: "START STEP"
              - id: gate
                type: HUMANGATE
                label: "APPROVAL STEP"
              - id: done
                type: END
                label: "DONE"
            edges:
              - id: e1
                from: start
                to: gate
              - id: e2
                from: gate
                to: done
            """;

        var def = new WorkflowDefinition
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "Mixed",
            Slug = "mixed", YamlContent = mixedCaseYaml, TriggerType = WorkflowTriggerType.Webhook,
            Status = WorkflowStatus.Published, CurrentVersion = "1",
        };
        var svc = BuildService(def);

        var dna = await svc.ComputeDnaAsync(def.Id, WorkspaceId);

        dna.HasHumanGates.Should().BeTrue();
        dna.NodeTypes.Should().Contain("humangate");
        dna.NodeTypes.Should().Contain("trigger");
        dna.NodeTypes.Should().Contain("end");
        dna.NodeCount.Should().Be(3);
    }

    [Fact]
    public async Task ComputeDnaAsync_InvalidYaml_ThrowsSfgParseException()
    {
        // Malformed YAML with missing required structure. Old regex silently returned empty DNA;
        // SfgParser correctly throws so the caller knows the workflow is broken.
        const string malformedYaml = "nodes: !!not-valid yaml: [unclosed";

        var def = new WorkflowDefinition
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "Bad",
            Slug = "bad", YamlContent = malformedYaml, TriggerType = WorkflowTriggerType.Webhook,
            Status = WorkflowStatus.Published, CurrentVersion = "1",
        };

        var defRepo = new Mock<IWorkflowDefinitionRepository>();
        defRepo.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(def);
        var metricRepo = new Mock<IWorkflowMetricRepository>();
        metricRepo.Setup(r => r.GetForDefinitionSinceAsync(It.IsAny<Guid>(), WorkspaceId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowMetric>());

        var svc = new WorkflowDnaService(defRepo.Object, metricRepo.Object, new SfgParser(), NullLogger<WorkflowDnaService>.Instance);

        var act = async () => await svc.ComputeDnaAsync(def.Id, WorkspaceId);

        await act.Should().ThrowAsync<SfgParseException>();
    }

    [Fact]
    public async Task ComputeDnaAsync_ActionNodeWithConnectorId_IncludedInDna()
    {
        const string connectorYaml = """
            workflow:
              id: connector-flow
              version: v1
              name: "Connector Flow"
            nodes:
              - id: start
                type: Trigger
                label: "Start"
              - id: send
                type: Action
                label: "Send Slack"
                config:
                  connector_id: slack
                  channel: "#alerts"
              - id: done
                type: End
                label: "Done"
            edges:
              - id: e1
                from: start
                to: send
              - id: e2
                from: send
                to: done
            """;

        var def = new WorkflowDefinition
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "Connector Flow",
            Slug = "connector-flow", YamlContent = connectorYaml, TriggerType = WorkflowTriggerType.Webhook,
            Status = WorkflowStatus.Published, CurrentVersion = "1",
        };
        var svc = BuildService(def);

        var dna = await svc.ComputeDnaAsync(def.Id, WorkspaceId);

        dna.ConnectorIds.Should().Contain("slack");
        dna.NodeTypes.Should().Contain("action");
    }

    [Fact]
    public async Task FindSimilarAsync_SameNodeTypesAndConnectors_HighScore()
    {
        // Regression: similarity scoring works correctly after the YamlDotNet refactor.
        var wf1 = new WorkflowDefinition
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "Flow A",
            Slug = "flow-a", YamlContent = SimpleYaml, TriggerType = WorkflowTriggerType.Webhook,
            Status = WorkflowStatus.Published, CurrentVersion = "1",
        };
        var wf2 = new WorkflowDefinition
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Name = "Flow B",
            Slug = "flow-b", YamlContent = SimpleYaml, TriggerType = WorkflowTriggerType.Webhook,
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

        var svc = new WorkflowDnaService(defRepo.Object, metricRepo.Object, new SfgParser(), NullLogger<WorkflowDnaService>.Instance);
        var dna = await svc.ComputeDnaAsync(wf1.Id, WorkspaceId);
        var similar = await svc.FindSimilarAsync(WorkspaceId, dna, 10);

        similar.Should().HaveCount(1);
        similar[0].SimilarityScore.Should().BeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public async Task DnaHash_SameWorkflow_Deterministic()
    {
        // SHA256 hash is stable across multiple ComputeDna calls for the same YAML.
        var svc = BuildService();
        var wfId = Guid.NewGuid();

        var dna1 = await svc.ComputeDnaAsync(wfId, WorkspaceId);
        var dna2 = await svc.ComputeDnaAsync(wfId, WorkspaceId);

        dna1.DnaHash.Should().NotBeNullOrEmpty();
        dna1.DnaHash.Should().HaveLength(16);
        dna1.DnaHash.Should().Be(dna2.DnaHash);
    }
}
