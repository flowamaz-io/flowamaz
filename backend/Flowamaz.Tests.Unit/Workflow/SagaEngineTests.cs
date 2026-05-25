using FluentAssertions;
using Flowamaz.Application.Workflow.Saga;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Saga engine behaviour (prompt 02-03): Backward compensates completed nodes in reverse execution
/// order (C→B→A), and Forward resets the failed node and re-queues the instance.
/// </summary>
public class SagaEngineTests
{
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowNodeStateRepository> _nodeStates = new();
    private readonly Mock<IWorkflowVariableRepository> _variables = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<INodeWorkerRegistry> _registry = new();
    private readonly Mock<ITaskQueue> _queue = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();

    private const string Yaml = """
        workflow: { id: w, version: v1, name: W }
        nodes:
          - { id: start, type: Trigger }
          - { id: A, type: Action, compensation: { strategy: backward, compensateNodeId: compA } }
          - { id: B, type: Action, compensation: { strategy: backward, compensateNodeId: compB } }
          - { id: C, type: Action, compensation: { strategy: backward, compensateNodeId: compC } }
          - { id: compA, type: Action }
          - { id: compB, type: Action }
          - { id: compC, type: Action }
          - { id: done, type: End }
        edges:
          - { from: start, to: A }
          - { from: A, to: B }
          - { from: B, to: C }
          - { from: C, to: done }
          - { from: start, to: compA }
          - { from: start, to: compB }
          - { from: start, to: compC }
        """;

    public SagaEngineTests()
    {
        _instances.Setup(r => r.GetByIdAsync(_instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance
            {
                Id = _instanceId, WorkspaceId = _workspaceId, WorkflowVersionId = _versionId, Status = InstanceStatus.Running,
            });
        _versions.Setup(r => r.GetByIdForWorkspaceAsync(_versionId, _workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _workspaceId, YamlContent = Yaml });
        _variables.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(r => r.GetNextSequenceNumberAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private SagaEngine NewEngine() => new(
        _instances.Object, _nodeStates.Object, _variables.Object, _versions.Object, _events.Object,
        _registry.Object, _queue.Object, _unitOfWork.Object, new SfgParser(), NullLogger<SagaEngine>.Instance);

    [Fact]
    public async Task Backward_compensates_completed_nodes_in_reverse_order()
    {
        var baseTime = DateTime.UtcNow;
        _nodeStates.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Completed("A", baseTime.AddSeconds(1)),
                Completed("B", baseTime.AddSeconds(2)),
                Completed("C", baseTime.AddSeconds(3)),
            ]);

        var recorder = new RecordingWorker();
        _registry.Setup(r => r.Resolve(NodeType.Action)).Returns(recorder);

        await NewEngine().StartAsync(_instanceId, "C", SagaStrategyType.Backward);

        recorder.Executed.Should().ContainInOrder("compC", "compB", "compA");
    }

    [Fact]
    public async Task Forward_resets_failed_node_and_requeues()
    {
        var failed = new WorkflowNodeState
        {
            InstanceId = _instanceId, WorkspaceId = _workspaceId, NodeId = "C", NodeType = "Action",
            Status = NodeStatus.Failed, RetryCount = 3,
        };
        _nodeStates.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([failed]);

        await NewEngine().StartAsync(_instanceId, "C", SagaStrategyType.Forward);

        failed.Status.Should().Be(NodeStatus.Pending);
        failed.RetryCount.Should().Be(0);
        _queue.Verify(q => q.EnqueueAsync(_workspaceId, _instanceId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private WorkflowNodeState Completed(string nodeId, DateTime completedAt) => new()
    {
        InstanceId = _instanceId, WorkspaceId = _workspaceId, NodeId = nodeId, NodeType = "Action",
        Status = NodeStatus.Completed, CompletedAt = completedAt,
    };

    private sealed class RecordingWorker : INodeWorker
    {
        public List<string> Executed { get; } = [];
        public NodeType SupportedType => NodeType.Action;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
        {
            Executed.Add(context.Node.Id);
            return Task.FromResult(NodeExecutionResult.Ok(null));
        }
    }
}
