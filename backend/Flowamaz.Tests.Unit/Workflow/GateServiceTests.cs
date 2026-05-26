using FluentAssertions;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// GateService decisions (fix-02-02): approving completes the gate node and resumes the run;
/// rejecting fails the node (triggering the failure/compensation path); both append a GateDecided
/// event. A decision on an already-decided gate is rejected, and cross-workspace access returns null.
/// </summary>
public class GateServiceTests
{
    private readonly Mock<IGateDecisionRepository> _gates = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<IWorkflowOrchestrator> _orchestrator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();
    private const string Node = "approve";

    private GateService NewService() => new(
        _gates.Object, _events.Object, _orchestrator.Object, _unitOfWork.Object, NullLogger<GateService>.Instance);

    private GateDecision Gate(GateDecisionStatus status = GateDecisionStatus.Pending) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = _ws, InstanceId = _instanceId, NodeId = Node, Decision = status,
    };

    [Fact]
    public async Task ListPendingAsync_MapsGates()
    {
        _gates.Setup(r => r.GetPendingForWorkspaceAsync(_ws, It.IsAny<CancellationToken>())).ReturnsAsync([Gate()]);
        var list = await NewService().ListPendingAsync(_ws);
        list.Should().ContainSingle().Which.NodeId.Should().Be(Node);
    }

    [Fact]
    public async Task GetAsync_WrongWorkspace_ReturnsNull()
    {
        var gate = Gate();
        gate.WorkspaceId = Guid.NewGuid(); // different workspace
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);

        (await NewService().GetAsync(_ws, _instanceId, Node)).Should().BeNull();
    }

    [Fact]
    public async Task DecideAsync_UnknownGate_ReturnsNull()
    {
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync((GateDecision?)null);
        (await NewService().DecideAsync(_ws, _instanceId, Node, "approved", null, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task DecideAsync_AlreadyDecided_Throws()
    {
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(Gate(GateDecisionStatus.Approved));

        var act = async () => await NewService().DecideAsync(_ws, _instanceId, Node, "approved", null, Guid.NewGuid());

        await act.Should().ThrowAsync<GateAlreadyDecidedException>();
    }

    [Fact]
    public async Task DecideAsync_Approved_CompletesNodeAndAppendsEvent()
    {
        var gate = Gate();
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);
        WorkflowEvent? appended = null;
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended = e).Returns(Task.CompletedTask);

        await NewService().DecideAsync(_ws, _instanceId, Node, "approved", "looks good", Guid.NewGuid());

        gate.Decision.Should().Be(GateDecisionStatus.Approved);
        appended!.EventType.Should().Be("GateDecided");
        _orchestrator.Verify(o => o.CompleteNodeAsync(_instanceId, Node, It.IsAny<string>(), "gate", It.IsAny<CancellationToken>()), Times.Once);
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecideAsync_Rejected_FailsNode()
    {
        var gate = Gate();
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);

        await NewService().DecideAsync(_ws, _instanceId, Node, "rejected", "budget exceeded", Guid.NewGuid());

        gate.Decision.Should().Be(GateDecisionStatus.Rejected);
        _orchestrator.Verify(o => o.FailNodeAsync(_instanceId, Node, It.Is<string>(s => s.Contains("budget exceeded")), "gate", It.IsAny<CancellationToken>()), Times.Once);
    }
}
