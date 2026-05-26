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
        _completion.Setup(c => c.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<ModelConfig, string, string, CancellationToken>((_, _, user, _) => Task.FromResult(new AiCompletionResult(user, 12, 8)));

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
    public async Task Unknown_instance_returns_null()
    {
        var result = await NewService().GenerateNarrativeAsync(_ws, Guid.NewGuid(), NarrativeAudience.Developer);
        result.Should().BeNull();
    }
}
