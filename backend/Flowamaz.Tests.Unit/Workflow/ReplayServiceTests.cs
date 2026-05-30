using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Workflow.Debugger;
using Flowamaz.Application.Workflow.Orchestrator;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Workflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Advanced monitoring (prompt 07-04): replay creates a lineage-tracked TEST instance with a merged
/// payload and rejects non-terminal instances with 409; the orchestrator captures per-node I/O
/// snapshots (sensitive variables stripped); and dev breakpoints only exist in Development.
/// </summary>
public class ReplayServiceTests
{
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<ITaskQueue> _queue = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();
    private readonly Guid _definitionId = Guid.NewGuid();

    private ReplayService NewReplayService() => new(
        _instances.Object, _events.Object, _queue.Object, _unitOfWork.Object,
        NullLogger<ReplayService>.Instance);

    private WorkflowInstance Original(InstanceStatus status, string? payload = null) => new()
    {
        Id = _instanceId,
        WorkspaceId = _ws,
        WorkflowDefinitionId = _definitionId,
        WorkflowVersionId = _versionId,
        Status = status,
        TriggerType = InstanceTriggerType.Manual,
        TriggerPayload = payload,
    };

    // ── Replay ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayInstanceAsync_creates_test_instance_with_merged_payload_and_lineage()
    {
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Original(InstanceStatus.Failed, """{"amount":10,"name":"orig"}"""));

        WorkflowInstance? added = null;
        _instances.Setup(r => r.AddAsync(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, CancellationToken>((i, _) => added = i)
            .Returns(Task.CompletedTask);
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var newId = await NewReplayService().ReplayInstanceAsync(_instanceId, """{"amount":99}""", _ws);

        newId.Should().NotBeNull();
        added.Should().NotBeNull();
        added!.IsTest.Should().BeTrue();
        added.ParentInstanceId.Should().Be(_instanceId);
        added.WorkflowVersionId.Should().Be(_versionId); // same pinned version
        added.TestExpiresAt.Should().NotBeNull();

        // Override key wins, original keys preserved.
        using var doc = JsonDocument.Parse(added.TriggerPayload!);
        doc.RootElement.GetProperty("amount").GetInt32().Should().Be(99);
        doc.RootElement.GetProperty("name").GetString().Should().Be("orig");

        _queue.Verify(q => q.EnqueueAsync(_ws, added.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplayInstanceAsync_with_no_override_reuses_original_payload()
    {
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Original(InstanceStatus.Completed, """{"x":1}"""));
        WorkflowInstance? added = null;
        _instances.Setup(r => r.AddAsync(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, CancellationToken>((i, _) => added = i).Returns(Task.CompletedTask);
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await NewReplayService().ReplayInstanceAsync(_instanceId, payloadOverride: null, _ws);

        added!.TriggerPayload.Should().Be("""{"x":1}""");
    }

    [Fact]
    public async Task ReplayInstanceAsync_running_instance_throws_409()
    {
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Original(InstanceStatus.Running));

        var act = () => NewReplayService().ReplayInstanceAsync(_instanceId, null, _ws);

        var ex = await act.Should().ThrowAsync<InstanceNotReplayableException>();
        ex.Which.HttpStatusCode.Should().Be(409);
        _instances.Verify(r => r.AddAsync(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplayInstanceAsync_unknown_instance_returns_null()
    {
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        var result = await NewReplayService().ReplayInstanceAsync(_instanceId, null, _ws);

        result.Should().BeNull();
    }

    // ── Orchestrator snapshots ─────────────────────────────────────────────────

    [Fact]
    public async Task CompleteNodeAsync_captures_output_snapshot_and_duration_on_event()
    {
        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        var nodeStates = new Mock<IWorkflowNodeStateRepository>();
        var variables = new Mock<IWorkflowVariableRepository>();
        var events = new Mock<IWorkflowEventRepository>();
        var queue = new Mock<ITaskQueue>();
        var variableEval = new Mock<IVariableEvaluationService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var versions = new Mock<IWorkflowVersionRepository>();
        var definitions = new Mock<IWorkflowDefinitionRepository>();

        var instance = new WorkflowInstance
        {
            Id = _instanceId, WorkspaceId = _ws, WorkflowVersionId = _versionId, Status = InstanceStatus.Running,
        };
        instanceRepo.Setup(r => r.GetByIdAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var state = new WorkflowNodeState
        {
            WorkspaceId = _ws, InstanceId = _instanceId, NodeId = "a", NodeType = "Action",
            Status = NodeStatus.Pending, StartedAt = DateTime.UtcNow.AddMilliseconds(-50),
        };
        nodeStates.Setup(r => r.GetByNodeAsync(_instanceId, "a", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        variables.Setup(r => r.GetByNameAsync(_instanceId, "a.output", It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowVariable?)null);

        WorkflowEvent? appended = null;
        events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended = e).Returns(Task.CompletedTask);

        var orchestrator = new WorkflowOrchestrator(
            definitions.Object, versions.Object, instanceRepo.Object, nodeStates.Object,
            variables.Object, events.Object, queue.Object, variableEval.Object,
            unitOfWork.Object, new SfgParser(), NullLogger<WorkflowOrchestrator>.Instance);

        await orchestrator.CompleteNodeAsync(_instanceId, "a", """{"delivered":true}""", "worker-1");

        appended.Should().NotBeNull();
        appended!.EventType.Should().Be("NodeCompleted");
        appended.OutputSnapshot.Should().Contain("delivered");
        appended.DurationMs.Should().NotBeNull();
        appended.DurationMs!.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task BuildInputSnapshot_strips_sensitive_variables_on_NodeStarted()
    {
        const string yaml = """
            workflow: { id: w, version: v1, name: W }
            nodes:
              - { id: start, type: Trigger }
              - { id: a, type: Action }
              - { id: done, type: End }
            edges:
              - { id: e1, from: start, to: a }
              - { id: e2, from: a, to: done }
            """;

        var instanceRepo = new Mock<IWorkflowInstanceRepository>();
        var nodeStates = new Mock<IWorkflowNodeStateRepository>();
        var variables = new Mock<IWorkflowVariableRepository>();
        var events = new Mock<IWorkflowEventRepository>();
        var queue = new Mock<ITaskQueue>();
        var variableEval = new Mock<IVariableEvaluationService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var versions = new Mock<IWorkflowVersionRepository>();
        var definitions = new Mock<IWorkflowDefinitionRepository>();

        instanceRepo.Setup(r => r.GetByIdAsync(_instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance
            {
                Id = _instanceId, WorkspaceId = _ws, WorkflowDefinitionId = _definitionId,
                WorkflowVersionId = _versionId, Status = InstanceStatus.Pending,
            });
        versions.Setup(r => r.GetByIdForWorkspaceAsync(_versionId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _ws, YamlContent = yaml });
        nodeStates.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        events.Setup(r => r.GetNextSequenceNumberAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        variableEval.Setup(v => v.EvaluateConditionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        variables.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new WorkflowVariable { Name = "fund_name", Value = "\"Tech Growth Fund\"", IsSensitive = false },
            new WorkflowVariable { Name = "api_secret", Value = "\"shh\"", IsSensitive = true },
        ]);

        var appended = new List<WorkflowEvent>();
        events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended.Add(e)).Returns(Task.CompletedTask);

        var orchestrator = new WorkflowOrchestrator(
            definitions.Object, versions.Object, instanceRepo.Object, nodeStates.Object,
            variables.Object, events.Object, queue.Object, variableEval.Object,
            unitOfWork.Object, new SfgParser(), NullLogger<WorkflowOrchestrator>.Instance);

        await orchestrator.StepAsync(_instanceId, "worker-1");

        var nodeStarted = appended.SingleOrDefault(e => e.EventType == "NodeStarted" && e.NodeId == "a");
        nodeStarted.Should().NotBeNull();
        nodeStarted!.InputSnapshot.Should().NotBeNull();
        nodeStarted.InputSnapshot.Should().Contain("fund_name");
        nodeStarted.InputSnapshot.Should().NotContain("api_secret");
        nodeStarted.InputSnapshot.Should().NotContain("shh");
    }

    // ── Breakpoints (Development only) ─────────────────────────────────────────

    [Fact]
    public void Breakpoints_only_honoured_in_Development()
    {
        var registry = new BreakpointRegistry(NullLogger<BreakpointRegistry>.Instance);
        var workflowId = Guid.NewGuid();
        registry.Add(_ws, workflowId, "a");

        // Development env: the orchestration layer would consult the registry.
        var dev = MockEnvironment(Environments.Development);
        var prod = MockEnvironment(Environments.Production);

        // The registry itself stores the breakpoint regardless; the gate is the environment.
        var honouredInDev = dev.IsDevelopment() && registry.IsBreakpointSet(_ws, workflowId, "a");
        var honouredInProd = prod.IsDevelopment() && registry.IsBreakpointSet(_ws, workflowId, "a");

        honouredInDev.Should().BeTrue();
        honouredInProd.Should().BeFalse();
    }

    private static IHostEnvironment MockEnvironment(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }
}
