using FluentAssertions;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// InstanceService read models + retry (fix-02-02): sensitive variable values are always masked,
/// the timeline orders nodes by start and computes offsets, and retry creates a fresh instance
/// pinned to the same version. Every read returns null for an instance outside the workspace.
/// </summary>
public class InstanceServiceTests
{
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowNodeStateRepository> _nodeStates = new();
    private readonly Mock<IWorkflowVariableRepository> _variables = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<ITaskQueue> _queue = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();

    private InstanceService NewService() => new(
        _instances.Object, _nodeStates.Object, _variables.Object, _events.Object,
        _queue.Object, _unitOfWork.Object, _versions.Object, new SfgParser(),
        NullLogger<InstanceService>.Instance);

    private WorkflowInstance Instance(InstanceStatus status = InstanceStatus.Running) => new()
    {
        Id = _instanceId,
        WorkspaceId = _ws,
        WorkflowDefinitionId = Guid.NewGuid(),
        WorkflowVersionId = _versionId,
        Status = status,
        TriggerType = InstanceTriggerType.Manual,
        StartedAt = new DateTime(2026, 3, 14, 10, 0, 0, DateTimeKind.Utc),
    };

    private void SetupInstance(WorkflowInstance instance) =>
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

    [Fact]
    public async Task GetDetailAsync_MasksSensitiveVariableValues()
    {
        SetupInstance(Instance());
        _nodeStates.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _variables.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new WorkflowVariable { WorkspaceId = _ws, InstanceId = _instanceId, Name = "api_key", Value = "secret-123", IsSensitive = true },
            new WorkflowVariable { WorkspaceId = _ws, InstanceId = _instanceId, Name = "region", Value = "eu-west-1", IsSensitive = false },
        ]);

        var detail = await NewService().GetDetailAsync(_ws, _instanceId);

        detail.Should().NotBeNull();
        detail!.Variables.Single(v => v.Name == "api_key").Value.Should().Be("***");
        detail.Variables.Single(v => v.Name == "region").Value.Should().Be("eu-west-1");
    }

    [Fact]
    public async Task GetDetailAsync_UnknownInstance_ReturnsNull()
    {
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        (await NewService().GetDetailAsync(_ws, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsLast50Events()
    {
        SetupInstance(Instance());
        _nodeStates.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _variables.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var events = Enumerable.Range(1, 150)
            .Select(i => new WorkflowEvent { WorkspaceId = _ws, InstanceId = _instanceId, SequenceNumber = i, EventType = "NodeCompleted", OccurredAt = DateTime.UtcNow })
            .ToList();
        _events.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(events);

        var detail = await NewService().GetDetailAsync(_ws, _instanceId);

        detail!.Events.Should().HaveCount(50);
        detail.Events.First().SequenceNumber.Should().Be(101);
        detail.Events.Last().SequenceNumber.Should().Be(150);
    }

    [Fact]
    public async Task GetVariablesAsync_MasksSensitive_AndNullWhenMissing()
    {
        SetupInstance(Instance());
        _variables.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new WorkflowVariable { WorkspaceId = _ws, InstanceId = _instanceId, Name = "token", Value = "abc", IsSensitive = true },
        ]);

        var vars = await NewService().GetVariablesAsync(_ws, _instanceId);
        vars.Should().NotBeNull();
        var single = vars!.Single();
        single.Value.Should().Be("***");
        single.IsSensitive.Should().BeTrue();

        _instances.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);
        (await NewService().GetVariablesAsync(_ws, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsAll_AndNullWhenMissing()
    {
        SetupInstance(Instance());
        _events.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new WorkflowEvent { WorkspaceId = _ws, InstanceId = _instanceId, SequenceNumber = 1, EventType = "InstanceStarted", OccurredAt = DateTime.UtcNow },
            new WorkflowEvent { WorkspaceId = _ws, InstanceId = _instanceId, SequenceNumber = 2, EventType = "InstanceCompleted", OccurredAt = DateTime.UtcNow },
        ]);

        var events = await NewService().GetEventsAsync(_ws, _instanceId);
        events.Should().HaveCount(2);

        _instances.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);
        (await NewService().GetEventsAsync(_ws, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_FiltersByStatus()
    {
        var keep = new WorkflowInstance { Id = Guid.NewGuid(), WorkspaceId = _ws, Status = InstanceStatus.Completed, CreatedAt = DateTime.UtcNow };
        var drop = new WorkflowInstance { Id = Guid.NewGuid(), WorkspaceId = _ws, Status = InstanceStatus.Running, CreatedAt = DateTime.UtcNow };
        _instances.Setup(r => r.GetForWorkspaceAsync(_ws, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync([keep, drop]);

        var list = await NewService().ListAsync(_ws, InstanceStatus.Completed, null, null, null);

        list.Should().ContainSingle().Which.Id.Should().Be(keep.Id);
    }

    [Fact]
    public async Task GetTimelineAsync_OrdersNodesByStart_AndComputesOffsets()
    {
        var instance = Instance();
        SetupInstance(instance);
        _versions.Setup(r => r.GetByIdForWorkspaceAsync(_versionId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowVersion?)null); // labels fall back to node id
        var start = instance.StartedAt!.Value;
        _nodeStates.Setup(r => r.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new WorkflowNodeState { WorkspaceId = _ws, InstanceId = _instanceId, NodeId = "second", NodeType = "Action", Status = NodeStatus.Completed, StartedAt = start.AddSeconds(10), CompletedAt = start.AddSeconds(15) },
            new WorkflowNodeState { WorkspaceId = _ws, InstanceId = _instanceId, NodeId = "first", NodeType = "Action", Status = NodeStatus.Completed, StartedAt = start.AddSeconds(2), CompletedAt = start.AddSeconds(5) },
        ]);

        var timeline = await NewService().GetTimelineAsync(_ws, _instanceId);

        timeline.Should().NotBeNull();
        timeline!.Nodes.Select(n => n.NodeId).Should().ContainInOrder("first", "second");
        var first = timeline.Nodes[0];
        first.DurationMs.Should().Be(3000);
        first.OffsetMs.Should().Be(2000);
        first.Label.Should().Be("first"); // fallback to node id when version missing
    }

    [Fact]
    public async Task GetTimelineAsync_UnknownInstance_ReturnsNull()
    {
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        (await NewService().GetTimelineAsync(_ws, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task RetryAsync_CreatesNewInstancePinnedToSameVersion_AndEnqueues()
    {
        var original = Instance(InstanceStatus.Failed);
        SetupInstance(original);
        WorkflowInstance? added = null;
        _instances.Setup(r => r.AddAsync(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, CancellationToken>((i, _) => added = i)
            .Returns(Task.CompletedTask);
        WorkflowEvent? appended = null;
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended = e)
            .Returns(Task.CompletedTask);

        var result = await NewService().RetryAsync(_ws, _instanceId);

        result.Should().NotBeNull();
        added.Should().NotBeNull();
        added!.WorkflowVersionId.Should().Be(_versionId); // same pinned version
        added.Status.Should().Be(InstanceStatus.Pending);
        appended!.EventType.Should().Be("InstanceStarted");
        _queue.Verify(q => q.EnqueueAsync(_ws, added.Id, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryAsync_UnknownInstance_ReturnsNull()
    {
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        (await NewService().RetryAsync(_ws, Guid.NewGuid())).Should().BeNull();
        _instances.Verify(r => r.AddAsync(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
