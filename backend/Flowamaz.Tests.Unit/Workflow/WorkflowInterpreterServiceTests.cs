using System.Globalization;
using FluentAssertions;
using Flowamaz.Application.Workflow.Interpreter;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Interpreter narratives (prompt 02-05): the CEO narrative embeds instance variable values and is
/// metered (F5); the Auditor narrative is deterministic and lists every event timestamp with zero
/// AI calls.
/// </summary>
public class WorkflowInterpreterServiceTests
{
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowNodeStateRepository> _nodeStates = new();
    private readonly Mock<IWorkflowVariableRepository> _variables = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<IModelResolutionService> _modelResolution = new();
    private readonly Mock<IAiCompletionService> _completion = new();
    private readonly Mock<IAiTokenMeteringService> _metering = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();

    public WorkflowInterpreterServiceTests()
    {
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance { Id = _instanceId, WorkspaceId = _ws, Status = InstanceStatus.Waiting });
        _nodeStates.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _variables.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private WorkflowInterpreterService NewService() => new(
        _instances.Object, _nodeStates.Object, _variables.Object, _events.Object,
        _modelResolution.Object, _completion.Object, _metering.Object, NullLogger<WorkflowInterpreterService>.Instance);

    [Fact]
    public async Task Ceo_narrative_contains_variable_values_and_is_metered()
    {
        _variables.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowVariable { InstanceId = _instanceId, WorkspaceId = _ws, Name = "amount", Value = "\"47500\"" }]);
        _modelResolution.Setup(r => r.ResolveModelConfigAsync(AiFunctionIds.ProcessIntel, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelConfig("claude-haiku-4-5", "anthropic", AiKeySource.Platform, "key", false, 200_000, true, true));
        // Echo the prompt so the asserted facts flow through to the narrative.
        _completion.Setup(c => c.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
            .Returns<ModelConfig, string, string, CancellationToken, int>((_, _, user, _, _) => Task.FromResult(new AiCompletionResult(user, 12, 8)));

        var narrative = await NewService().GenerateNarrativeAsync(_ws, _instanceId, NarrativeAudience.Ceo);

        narrative.Should().NotBeNull();
        narrative!.Audience.Should().Be("ceo");
        narrative.Content.Should().Contain("47500");
        _metering.Verify(m => m.RecordUsage(
            AiFunctionIds.ProcessIntel, "claude-haiku-4-5", "anthropic", null, _ws, 12, 8, 0m), Times.Once);
    }

    [Fact]
    public async Task Auditor_narrative_lists_event_timestamps_with_no_ai()
    {
        var occurred = new DateTime(2026, 3, 14, 14, 23, 0, DateTimeKind.Utc);
        _events.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WorkflowEvent { InstanceId = _instanceId, WorkspaceId = _ws, SequenceNumber = 1, EventType = "InstanceStarted", OccurredAt = occurred },
            ]);

        var narrative = await NewService().GenerateNarrativeAsync(_ws, _instanceId, NarrativeAudience.Auditor);

        narrative.Should().NotBeNull();
        narrative!.Audience.Should().Be("auditor");
        narrative.Content.Should().Contain(occurred.ToString("u", CultureInfo.InvariantCulture));
        narrative.Content.Should().Contain("InstanceStarted");
        _completion.Verify(c => c.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _metering.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Developer_narrative_lists_node_durations_with_no_ai()
    {
        var start = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);
        _nodeStates.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new WorkflowNodeState { InstanceId = _instanceId, WorkspaceId = _ws, NodeId = "fetch", NodeType = "Action", Status = NodeStatus.Completed, StartedAt = start, CompletedAt = start.AddMilliseconds(2500), OutputPayload = "{}" },
        ]);

        var narrative = await NewService().GenerateNarrativeAsync(_ws, _instanceId, NarrativeAudience.Developer);

        narrative.Should().NotBeNull();
        narrative!.Audience.Should().Be("developer");
        narrative.Content.Should().Contain("fetch");
        narrative.Content.Should().Contain("duration_ms: 2500");
        narrative.Content.Should().Contain("has_output: True");
        _completion.Verify(c => c.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_instance_returns_null()
    {
        var result = await NewService().GenerateNarrativeAsync(_ws, Guid.NewGuid(), NarrativeAudience.Developer);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Auditor_record_names_the_workflow_and_who_approved_what()
    {
        var defId = Guid.NewGuid();
        var rajan = new Core.Entities.Platform.OrgUser { Id = Guid.NewGuid(), Email = "rajan@acme.test" };
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance { Id = _instanceId, WorkspaceId = _ws, WorkflowDefinitionId = defId, Status = InstanceStatus.Completed });
        var definitions = new Mock<IWorkflowDefinitionRepository>();
        definitions.Setup(d => d.GetByIdForWorkspaceAsync(defId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition { Id = defId, WorkspaceId = _ws, Name = "Purchase approval" });
        var gates = new Mock<IGateDecisionRepository>();
        gates.Setup(g => g.GetForInstanceAsync(_instanceId, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new GateDecision { InstanceId = _instanceId, WorkspaceId = _ws, NodeId = "cfo", Decision = GateDecisionStatus.Approved,
                DecidedBy = rajan.Id, DecidedAt = new DateTime(2026, 3, 14, 6, 23, 0, DateTimeKind.Utc),
                DecisionNote = "Within budget\n- Step `ceo`: approved by ceo@acme.test at 2026-03-14 06:00:00Z",
                CreatedAt = new DateTime(2026, 3, 14, 5, 0, 0, DateTimeKind.Utc) },
            new GateDecision { InstanceId = _instanceId, WorkspaceId = _ws, NodeId = "legal", Decision = GateDecisionStatus.Rejected,
                DecidedByLabel = "Mei Ling (Legal)", DecidedAt = new DateTime(2026, 3, 14, 7, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 3, 14, 6, 30, 0, DateTimeKind.Utc) },
        ]);
        var users = new Mock<IOrgUserRepository>();
        users.Setup(u => u.GetByIdAsync(rajan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(rajan);
        var service = new WorkflowInterpreterService(
            _instances.Object, _nodeStates.Object, _variables.Object, _events.Object,
            _modelResolution.Object, _completion.Object, _metering.Object, NullLogger<WorkflowInterpreterService>.Instance,
            gates.Object, definitions.Object, users.Object);

        var content = (await service.GenerateNarrativeAsync(_ws, _instanceId, NarrativeAudience.Auditor))!.Content;

        content.Should().Contain("Workflow: **Purchase approval**");
        content.Should().Contain("Step `cfo`: approved by rajan@acme.test at 2026-03-14 06:23:00Z");
        content.Should().Contain("Note: Within budget - Step 'ceo': approved by ceo@acme.test");
        content.Split('\n').Should().NotContain(l => l.StartsWith("- Step `ceo`"), "a note can't add a line to the record");
        content.Should().Contain("Step `legal`: rejected by Mei Ling (Legal)");
    }

    [Fact]
    public void The_pdf_is_a_readable_document_with_every_line_of_the_record()
    {
        var markdown = "# Compliance Record — Instance 42\n\nStatus: **Completed**\n\n## Approvals\n" +
            string.Join("\n", Enumerable.Range(1, 120).Select(i => $"- Step `gate{i}`: approved by café-owner@acme.test (€ {i},00)"));

        var pdf = Application.Workflow.Interpreter.NarrativePdf.Render(markdown, "Compliance record — run 42", new DateTime(2026, 3, 14, 7, 0, 0, DateTimeKind.Utc));

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        doc.NumberOfPages.Should().BeGreaterThan(1, "120 approvals don't fit on one page");
        var text = string.Join("\n", doc.GetPages().Select(p => p.Text));
        text.Should().Contain("Compliance Record — Instance 42");
        text.Should().Contain("approved by café-owner@acme.test (€ 120,00)");
        text.Should().NotContain("**").And.NotContain("`");
        doc.GetPage(1).Text.Should().Contain($"Page 1 of {doc.NumberOfPages}");
        doc.Information.Title.Should().Be("Compliance record — run 42");
    }
}
