using FluentAssertions;
using Flowamaz.Application.Workflow.Debugger;
using Flowamaz.Application.Workflow.Orchestrator;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Orchestrator behaviour with mocked persistence/queue and a real <see cref="SfgParser"/>:
/// trigger creates + enqueues an instance and emits InstanceStarted, idempotency dedups, parallel
/// nodes fan out, and routers select only the matching branch.
/// </summary>
public class WorkflowOrchestratorTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowNodeStateRepository> _nodeStates = new();
    private readonly Mock<IWorkflowVariableRepository> _variables = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<ITaskQueue> _queue = new();
    private readonly Mock<IVariableEvaluationService> _variableEvaluation = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _definitionId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();

    private WorkflowOrchestrator NewOrchestrator() => new(
        _definitions.Object, _versions.Object, _instances.Object, _nodeStates.Object,
        _variables.Object, _events.Object, _queue.Object, _variableEvaluation.Object,
        _unitOfWork.Object, new SfgParser(), NullLogger<WorkflowOrchestrator>.Instance);

    [Fact]
    public async Task TriggerAsync_creates_instance_records_event_and_enqueues()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_definitionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition { Id = _definitionId, WorkspaceId = _workspaceId, Name = "W", Slug = "w", Status = WorkflowStatus.Published });
        _versions.Setup(r => r.GetProductionAsync(_definitionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _workspaceId, WorkflowDefinitionId = _definitionId });

        WorkflowEvent? appended = null;
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended = e)
            .Returns(Task.CompletedTask);

        var instance = await NewOrchestrator().TriggerAsync(_workspaceId, _definitionId, """{"x":1}""", idempotencyKey: null);

        instance.Status.Should().Be(InstanceStatus.Pending);
        instance.WorkflowVersionId.Should().Be(_versionId);
        appended.Should().NotBeNull();
        appended!.EventType.Should().Be("InstanceStarted");
        _instances.Verify(r => r.AddAsync(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()), Times.Once);
        _queue.Verify(q => q.EnqueueAsync(_workspaceId, instance.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TriggerAsync_test_run_sets_IsTest_and_TestExpiresAt()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_definitionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition { Id = _definitionId, WorkspaceId = _workspaceId, Name = "W", Slug = "w", Status = WorkflowStatus.Draft });
        _versions.Setup(r => r.GetProductionAsync(_definitionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowVersion?)null);
        _versions.Setup(r => r.GetLatestAsync(_definitionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _workspaceId, WorkflowDefinitionId = _definitionId });

        WorkflowInstance? added = null;
        _instances.Setup(r => r.AddAsync(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, CancellationToken>((i, _) => added = i)
            .Returns(Task.CompletedTask);
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var instance = await NewOrchestrator().TriggerAsync(
            _workspaceId, _definitionId, null, null, isTest: true);

        instance.IsTest.Should().BeTrue();
        instance.TestExpiresAt.Should().NotBeNull();
        instance.TestExpiresAt!.Value.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), precision: TimeSpan.FromSeconds(5));
        added.Should().NotBeNull();
        added!.IsTest.Should().BeTrue();
    }

    [Fact]
    public async Task TriggerAsync_production_run_rejects_draft_workflow()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_definitionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition { Id = _definitionId, WorkspaceId = _workspaceId, Name = "W", Slug = "w", Status = WorkflowStatus.Draft });

        var orchestrator = NewOrchestrator();
        await Assert.ThrowsAsync<WorkflowNotPublishedException>(
            () => orchestrator.TriggerAsync(_workspaceId, _definitionId, null, null, isTest: false));
    }

    [Fact]
    public async Task TriggerAsync_with_duplicate_idempotency_key_returns_existing_and_creates_nothing()
    {
        var existing = new WorkflowInstance { Id = Guid.NewGuid(), WorkspaceId = _workspaceId, IdempotencyKey = "dup" };
        _instances.Setup(r => r.GetByIdempotencyKeyAsync(_workspaceId, "dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await NewOrchestrator().TriggerAsync(_workspaceId, _definitionId, null, idempotencyKey: "dup");

        result.Id.Should().Be(existing.Id);
        _instances.Verify(r => r.AddAsync(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()), Times.Never);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StepAsync_fans_out_parallel_branches()
    {
        const string yaml = """
            workflow: { id: w, version: v1, name: W }
            nodes:
              - { id: start, type: Trigger }
              - { id: fan, type: Parallel }
              - { id: a, type: Action }
              - { id: b, type: Action }
              - { id: done, type: End }
            edges:
              - { id: e1, from: start, to: fan }
              - { id: e2, from: fan, to: a }
              - { id: e3, from: fan, to: b }
              - { id: e4, from: a, to: done }
              - { id: e5, from: b, to: done }
            """;
        var instanceId = SetupRunnableInstance(yaml);
        AllConditionsTrue();

        var result = await NewOrchestrator().StepAsync(instanceId, "worker-1");

        result.IsComplete.Should().BeFalse();
        result.NextNodes.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task StepAsync_router_selects_only_matching_branch()
    {
        const string yaml = """
            workflow: { id: w, version: v1, name: W }
            nodes:
              - { id: start, type: Trigger }
              - { id: router, type: Router }
              - { id: approve, type: Action }
              - { id: reject, type: Action }
              - { id: done, type: End }
            edges:
              - { id: e1, from: start, to: router }
              - { id: e2, from: router, to: approve, condition: '{{ decision }} == "approve"' }
              - { id: e3, from: router, to: reject, condition: '{{ decision }} == "reject"' }
              - { id: e4, from: approve, to: done }
              - { id: e5, from: reject, to: done }
            """;
        var instanceId = SetupRunnableInstance(yaml);
        // Unconditional edges (null) pass; only the "approve" branch condition is true.
        _variableEvaluation.Setup(v => v.EvaluateConditionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string?, CancellationToken>((_, cond, _) =>
                Task.FromResult(string.IsNullOrWhiteSpace(cond) || cond.Contains("approve")));

        var result = await NewOrchestrator().StepAsync(instanceId, "worker-1");

        result.NextNodes.Should().ContainSingle().Which.Should().Be("approve");
    }

    [Fact]
    public async Task CompleteNodeAsync_marks_completed_stores_output_and_requeues()
    {
        var instanceId = Guid.NewGuid();
        var instance = new WorkflowInstance { Id = instanceId, WorkspaceId = _workspaceId, WorkflowVersionId = _versionId, Status = InstanceStatus.Running };
        _instances.Setup(r => r.GetByIdAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        var state = new WorkflowNodeState { WorkspaceId = _workspaceId, InstanceId = instanceId, NodeId = "a", NodeType = "Action", Status = NodeStatus.Pending };
        _nodeStates.Setup(r => r.GetByNodeAsync(instanceId, "a", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _variables.Setup(r => r.GetByNameAsync(instanceId, "a.output", It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowVariable?)null);
        WorkflowEvent? appended = null;
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended = e).Returns(Task.CompletedTask);

        await NewOrchestrator().CompleteNodeAsync(instanceId, "a", """{"ok":true}""", "worker-1");

        state.Status.Should().Be(NodeStatus.Completed);
        state.OutputPayload.Should().Contain("ok");
        _variables.Verify(r => r.AddAsync(It.Is<WorkflowVariable>(v => v.Name == "a.output"), It.IsAny<CancellationToken>()), Times.Once);
        appended!.EventType.Should().Be("NodeCompleted");
        _queue.Verify(q => q.EnqueueAsync(_workspaceId, instanceId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_running_instance_sets_cancelled_releases_lease_and_appends_event()
    {
        var instanceId = Guid.NewGuid();
        var instance = new WorkflowInstance { Id = instanceId, WorkspaceId = _workspaceId, Status = InstanceStatus.Running, WorkerLeaseId = "worker-1" };
        _instances.Setup(r => r.GetByIdAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        WorkflowEvent? appended = null;
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended = e).Returns(Task.CompletedTask);

        await NewOrchestrator().CancelAsync(instanceId, Guid.NewGuid());

        instance.Status.Should().Be(InstanceStatus.Cancelled);
        appended!.EventType.Should().Be("InstanceCancelled");
        _instances.Verify(r => r.ReleaseLeaseAsync(instanceId, "worker-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_completed_instance_is_noop()
    {
        var instanceId = Guid.NewGuid();
        var instance = new WorkflowInstance { Id = instanceId, WorkspaceId = _workspaceId, Status = InstanceStatus.Completed };
        _instances.Setup(r => r.GetByIdAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(instance);

        await NewOrchestrator().CancelAsync(instanceId, Guid.NewGuid());

        instance.Status.Should().Be(InstanceStatus.Completed);
        _events.Verify(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _instances.Verify(r => r.ReleaseLeaseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailNodeAsync_no_retry_no_compensation_fails_instance()
    {
        var instanceId = SetupNodeOps(ActionYaml(), out var instance, out var state);

        await NewOrchestrator().FailNodeAsync(instanceId, "a", "boom", "worker-1");

        state.Status.Should().Be(NodeStatus.Failed);
        instance.Status.Should().Be(InstanceStatus.Failed);
        instance.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task FailNodeAsync_with_retries_remaining_requeues_with_backoff()
    {
        const string yaml = """
            workflow: { id: w, version: v1, name: W }
            nodes:
              - { id: start, type: Trigger }
              - { id: a, type: Action, retry: { maxAttempts: 3, backoffSeconds: 1, backoffMultiplier: 2 } }
              - { id: done, type: End }
            edges:
              - { id: e1, from: start, to: a }
              - { id: e2, from: a, to: done }
            """;
        var instanceId = SetupNodeOps(yaml, out var instance, out var state);

        await NewOrchestrator().FailNodeAsync(instanceId, "a", "transient", "worker-1");

        state.RetryCount.Should().Be(1);
        state.Status.Should().Be(NodeStatus.Pending);
        instance.Status.Should().Be(InstanceStatus.Running); // not failed — retry pending
        _queue.Verify(q => q.EnqueueDelayedAsync(_workspaceId, instanceId, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailNodeAsync_with_compensation_no_saga_engine_marks_compensating()
    {
        const string yaml = """
            workflow: { id: w, version: v1, name: W }
            nodes:
              - { id: start, type: Trigger }
              - { id: a, type: Action, compensation: { strategy: backward, compensateNodeId: cA } }
              - { id: done, type: End }
            edges:
              - { id: e1, from: start, to: a }
              - { id: e2, from: a, to: done }
            """;
        var instanceId = SetupNodeOps(yaml, out var instance, out var state);
        var appended = new List<WorkflowEvent>();
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended.Add(e)).Returns(Task.CompletedTask);

        await NewOrchestrator().FailNodeAsync(instanceId, "a", "boom", "worker-1");

        instance.Status.Should().Be(InstanceStatus.Compensating);
        instance.SagaState.Should().Be(SagaState.Compensating);
        appended.Select(e => e.EventType).Should().Contain("CompensationStarted");
    }

    [Fact]
    public async Task StepAsync_with_breakpoint_set_pauses_instance_at_node()
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
        var (instanceId, instance) = SetupCapturedRunnableInstance(yaml);
        AllConditionsTrue();

        var registry = new BreakpointRegistry(NullLogger<BreakpointRegistry>.Instance);
        registry.Add(_workspaceId, _definitionId, "a");

        var result = await NewOrchestratorWithBreakpoints(registry, DevEnvironment).StepAsync(instanceId, "worker-1");

        instance.Status.Should().Be(InstanceStatus.BreakpointHit);
        instance.CurrentNodeId.Should().Be("a");
        result.IsComplete.Should().BeFalse();
        result.NextNodes.Should().BeEmpty();
    }

    [Fact]
    public async Task StepAsync_after_resume_clears_pause_and_surfaces_node()
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
        var (instanceId, instance) = SetupCapturedRunnableInstance(yaml);
        AllConditionsTrue();

        var registry = new BreakpointRegistry(NullLogger<BreakpointRegistry>.Instance);
        registry.Add(_workspaceId, _definitionId, "a");
        var orchestrator = NewOrchestratorWithBreakpoints(registry, DevEnvironment);

        // First step pauses at the breakpoint.
        await orchestrator.StepAsync(instanceId, "worker-1");
        instance.Status.Should().Be(InstanceStatus.BreakpointHit);

        // Resume clears the per-instance pause. The first step persisted the trigger as completed; the
        // breakpoint node "a" was never entered, so the frontier re-seeds "a".
        registry.MarkResumed(instanceId, "a");
        _nodeStates.Setup(r => r.GetForInstanceAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowNodeState
            {
                WorkspaceId = _workspaceId, InstanceId = instanceId, NodeId = "start",
                NodeType = "Trigger", Status = NodeStatus.Completed,
            }]);

        var result = await orchestrator.StepAsync(instanceId, "worker-1");

        instance.Status.Should().Be(InstanceStatus.Running);
        result.NextNodes.Should().ContainSingle().Which.Should().Be("a");
    }

    private static readonly IHostEnvironment DevEnvironment = new FakeHostEnvironment(Environments.Development);

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Flowamaz.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private WorkflowOrchestrator NewOrchestratorWithBreakpoints(IBreakpointRegistry breakpoints, IHostEnvironment environment) => new(
        _definitions.Object, _versions.Object, _instances.Object, _nodeStates.Object,
        _variables.Object, _events.Object, _queue.Object, _variableEvaluation.Object,
        _unitOfWork.Object, new SfgParser(), NullLogger<WorkflowOrchestrator>.Instance,
        breakpoints: breakpoints, environment: environment);

    private (Guid InstanceId, WorkflowInstance Instance) SetupCapturedRunnableInstance(string yaml)
    {
        var instanceId = Guid.NewGuid();
        var instance = new WorkflowInstance
        {
            Id = instanceId,
            WorkspaceId = _workspaceId,
            WorkflowDefinitionId = _definitionId,
            WorkflowVersionId = _versionId,
            Status = InstanceStatus.Pending,
        };
        _instances.Setup(r => r.GetByIdAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        _versions.Setup(r => r.GetByIdForWorkspaceAsync(_versionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _workspaceId, YamlContent = yaml });
        _nodeStates.Setup(r => r.GetForInstanceAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(r => r.GetNextSequenceNumberAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (instanceId, instance);
    }

    private static string ActionYaml() => """
        workflow: { id: w, version: v1, name: W }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    private Guid SetupNodeOps(string yaml, out WorkflowInstance instance, out WorkflowNodeState state)
    {
        var instanceId = Guid.NewGuid();
        instance = new WorkflowInstance { Id = instanceId, WorkspaceId = _workspaceId, WorkflowVersionId = _versionId, Status = InstanceStatus.Running };
        var captured = instance;
        _instances.Setup(r => r.GetByIdAsync(instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(captured);
        _versions.Setup(r => r.GetByIdForWorkspaceAsync(_versionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _workspaceId, YamlContent = yaml });
        state = new WorkflowNodeState { WorkspaceId = _workspaceId, InstanceId = instanceId, NodeId = "a", NodeType = "Action", Status = NodeStatus.Pending };
        var capturedState = state;
        _nodeStates.Setup(r => r.GetByNodeAsync(instanceId, "a", It.IsAny<CancellationToken>())).ReturnsAsync(capturedState);
        return instanceId;
    }

    private Guid SetupRunnableInstance(string yaml)
    {
        var instanceId = Guid.NewGuid();
        _instances.Setup(r => r.GetByIdAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance
            {
                Id = instanceId,
                WorkspaceId = _workspaceId,
                WorkflowDefinitionId = _definitionId,
                WorkflowVersionId = _versionId,
                Status = InstanceStatus.Pending,
            });
        _versions.Setup(r => r.GetByIdForWorkspaceAsync(_versionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _workspaceId, YamlContent = yaml });
        _nodeStates.Setup(r => r.GetForInstanceAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _events.Setup(r => r.GetNextSequenceNumberAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return instanceId;
    }

    private void AllConditionsTrue() =>
        _variableEvaluation.Setup(v => v.EvaluateConditionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
}
