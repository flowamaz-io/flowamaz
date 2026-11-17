using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
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
        _gates.Setup(r => r.GetPendingForWorkspaceAsync(_ws, false, It.IsAny<CancellationToken>())).ReturnsAsync([Gate()]);
        var list = await NewService().ListPendingAsync(_ws);
        list.Should().ContainSingle().Which.NodeId.Should().Be(Node);
        list[0].IsTest.Should().BeFalse();
    }

    [Fact]
    public async Task ListPendingAsync_includeTest_flags_test_run_gates()
    {
        // O2: test-run gates are opt-in and tagged so the queue can label them.
        _gates.Setup(r => r.GetPendingForWorkspaceAsync(_ws, true, It.IsAny<CancellationToken>())).ReturnsAsync([Gate()]);
        _gates.Setup(r => r.GetTestInstanceIdsAsync(_ws, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_instanceId]);

        var list = await NewService().ListPendingAsync(_ws, includeTest: true);

        list.Should().ContainSingle().Which.IsTest.Should().BeTrue();
    }

    [Fact]
    public async Task ListPendingAsync_default_excludes_test_runs_at_the_repository()
    {
        _gates.Setup(r => r.GetPendingForWorkspaceAsync(_ws, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await NewService().ListPendingAsync(_ws);

        _gates.Verify(r => r.GetPendingForWorkspaceAsync(_ws, false, It.IsAny<CancellationToken>()), Times.Once);
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
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task DecideAsync_Rejected_with_no_rejected_path_rejects_the_run()
    {
        var gate = Gate();
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);

        await NewService().DecideAsync(_ws, _instanceId, Node, "rejected", "budget exceeded", Guid.NewGuid());

        gate.Decision.Should().Be(GateDecisionStatus.Rejected);
        // B10: the run ends Rejected (never retried), not Failed.
        _orchestrator.Verify(o => o.RejectNodeAsync(_instanceId, Node, It.Is<string>(s => s.StartsWith("Gate rejected") && s.Contains("budget exceeded")), "gate", It.IsAny<CancellationToken>()), Times.Once);
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Theory]
    [InlineData("approve", GateDecisionStatus.Approved)]
    [InlineData(" Approved ", GateDecisionStatus.Approved)]
    [InlineData("reject", GateDecisionStatus.Rejected)]
    public async Task DecideAsync_accepts_both_spellings(string decision, GateDecisionStatus expected)
    {
        var gate = Gate();
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);

        await NewService().DecideAsync(_ws, _instanceId, Node, decision, null, Guid.NewGuid());

        gate.Decision.Should().Be(expected);
    }

    [Theory]
    [InlineData("aproved")]
    [InlineData("yes")]
    [InlineData("")]
    public async Task DecideAsync_refuses_anything_else_instead_of_rejecting(string decision)
    {
        var gate = Gate();
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);

        var act = () => NewService().DecideAsync(_ws, _instanceId, Node, decision, null, Guid.NewGuid());

        (await act.Should().ThrowAsync<GateDecisionValueException>()).Which.HttpStatusCode.Should().Be(400);
        gate.Decision.Should().Be(GateDecisionStatus.Pending, "a typo must not count as a rejection");
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    // ── decided_by (public API names the person who decided) ─────────────────────────────

    [Fact]
    public async Task DecideAsync_with_decided_by_stores_it_and_returns_it()
    {
        var gate = Gate();
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);

        var result = await NewService().DecideAsync(
            _ws, _instanceId, Node, "approved", null, Guid.Empty, decidedByLabel: "  Aisha Rahman (Branch Manager)  ");

        gate.DecidedBy.Should().BeNull();
        gate.DecidedByLabel.Should().Be("Aisha Rahman (Branch Manager)");
        result!.DecidedByLabel.Should().Be("Aisha Rahman (Branch Manager)");
    }

    [Fact]
    public async Task DecideAsync_with_decided_by_puts_it_on_the_event_as_valid_json()
    {
        var gate = Gate();
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);
        WorkflowEvent? appended = null;
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended = e).Returns(Task.CompletedTask);

        // Quotes and braces in caller text must not break (or inject into) the event payload.
        await NewService().DecideAsync(_ws, _instanceId, Node, "rejected", null, Guid.Empty,
            decidedByLabel: "Lee \"LJ\" {Credit}");

        using var doc = JsonDocument.Parse(appended!.Payload!);
        doc.RootElement.GetProperty("decision").GetString().Should().Be("Rejected");
        doc.RootElement.GetProperty("decided_by").GetString().Should().Be("Lee \"LJ\" {Credit}");
    }

    [Fact]
    public async Task DecideAsync_without_decided_by_keeps_the_event_payload_unchanged()
    {
        var gate = Gate();
        _gates.Setup(r => r.GetByNodeAsync(_instanceId, Node, It.IsAny<CancellationToken>())).ReturnsAsync(gate);
        WorkflowEvent? appended = null;
        _events.Setup(r => r.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowEvent, CancellationToken>((e, _) => appended = e).Returns(Task.CompletedTask);

        await NewService().DecideAsync(_ws, _instanceId, Node, "approved", null, Guid.NewGuid(), decidedByLabel: "   ");

        appended!.Payload.Should().Be("""{"decision":"Approved"}""");
        gate.DecidedByLabel.Should().BeNull();
    }
}
