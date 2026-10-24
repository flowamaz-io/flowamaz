using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;

namespace Flowamaz.Tests.Unit.Jobs;

/// <summary>
/// GateTimeoutJob (fix-02-01): an expired pending gate is marked Escalated with a GateDecided event;
/// with an escalation target it waits on that target, without one the gate node is failed. Errors
/// while processing a gate are logged, not propagated (a thrown job would stop the Quartz scheduler).
/// </summary>
public class GateTimeoutJobTests
{
    private readonly Mock<IGateDecisionRepository> _gates = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<IWorkflowOrchestrator> _orchestrator = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private GateTimeoutJob NewJob() =>
        new(_gates.Object, _events.Object, _orchestrator.Object, _unitOfWork.Object, NullLogger<GateTimeoutJob>.Instance);

    private static IJobExecutionContext Context()
    {
        var ctx = new Mock<IJobExecutionContext>();
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static GateDecision Gate(Guid? escalatedTo) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        InstanceId = Guid.NewGuid(),
        NodeId = "approve",
        Decision = GateDecisionStatus.Pending,
        ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
        EscalatedTo = escalatedTo,
    };

    [Fact]
    public async Task Execute_NoExpiredGates_DoesNothing()
    {
        _gates.Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await NewJob().Execute(Context());

        _gates.Verify(r => r.Update(It.IsAny<GateDecision>()), Times.Never);
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Escalate_ReassignsOnce_KeepsGatePendingAndDecidable_WithFreshWindow()
    {
        // Regression: escalation used to set Decision=Escalated, which DecideAsync rejects — the run hung.
        var gate = Gate(escalatedTo: null);
        gate.OnTimeout = GateTimeoutActions.Escalate;
        gate.EscalateToEmail = "director@example.com";
        gate.TimeoutHours = 24;
        _gates.Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([gate]);

        await NewJob().Execute(Context());

        gate.Decision.Should().Be(GateDecisionStatus.Pending);
        gate.AssignedToEmail.Should().Be("director@example.com");
        gate.EscalationCount.Should().Be(1);
        gate.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
        _events.Verify(e => e.AppendAsync(It.Is<WorkflowEvent>(w => w.EventType == "GateEscalated"), It.IsAny<CancellationToken>()), Times.Once);
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Escalate_SecondTimeout_FailsTheGate()
    {
        var gate = Gate(escalatedTo: null);
        gate.OnTimeout = GateTimeoutActions.Escalate;
        gate.EscalateToEmail = "director@example.com";
        gate.EscalationCount = 1;
        _gates.Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([gate]);

        await NewJob().Execute(Context());

        _orchestrator.Verify(o => o.FailNodeAsync(gate.InstanceId, gate.NodeId, It.Is<string>(m => m.Contains("after escalation")),
            It.IsAny<string>(), It.IsAny<CancellationToken>(), false), Times.Once);
    }

    [Fact]
    public async Task AutoApprove_RecordsApproval_AndCompletesTheNode()
    {
        var gate = Gate(escalatedTo: null);
        gate.OnTimeout = GateTimeoutActions.AutoApprove;
        _gates.Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([gate]);

        await NewJob().Execute(Context());

        gate.Decision.Should().Be(GateDecisionStatus.Approved);
        gate.DecisionNote.Should().Contain("Auto-approved");
        _orchestrator.Verify(o => o.CompleteNodeAsync(gate.InstanceId, gate.NodeId, It.Is<string>(j => j.Contains("approved")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task AutoReject_RecordsRejection_AndFailsWithoutRetry()
    {
        var gate = Gate(escalatedTo: null);
        gate.OnTimeout = GateTimeoutActions.AutoReject;
        _gates.Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([gate]);

        await NewJob().Execute(Context());

        gate.Decision.Should().Be(GateDecisionStatus.Rejected);
        _orchestrator.Verify(o => o.FailNodeAsync(gate.InstanceId, gate.NodeId, It.Is<string>(m => m.Contains("auto-rejected")),
            It.IsAny<string>(), It.IsAny<CancellationToken>(), false), Times.Once);
    }

    [Fact]
    public async Task Execute_ExpiredGateNoEscalationTarget_FailsGateNode()
    {
        var gate = Gate(escalatedTo: null);
        gate.OnTimeout = GateTimeoutActions.Escalate; // escalate requested but no target → fail
        _gates.Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([gate]);

        await NewJob().Execute(Context());

        gate.Decision.Should().Be(GateDecisionStatus.Escalated);
        // A timed-out gate must not be retried (that would open a new approval).
        _orchestrator.Verify(o => o.FailNodeAsync(gate.InstanceId, gate.NodeId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), false), Times.Once);
    }

    [Theory]
    [InlineData("auto_approve", GateTimeoutActions.AutoApprove)]
    [InlineData("Auto-Reject", GateTimeoutActions.AutoReject)]
    [InlineData("escalate", GateTimeoutActions.Escalate)]
    [InlineData(null, GateTimeoutActions.Fail)]
    [InlineData("whatever", GateTimeoutActions.Fail)]
    public void TimeoutAction_IsNormalised(string? input, string expected) =>
        GateTimeoutActions.Normalize(input).Should().Be(expected);

    [Fact]
    public async Task Execute_AppendThrows_LogsAndDoesNotPropagate()
    {
        _gates.Setup(r => r.GetExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Gate(escalatedTo: null)]);
        _events.Setup(e => e.AppendAsync(It.IsAny<WorkflowEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var act = async () => await NewJob().Execute(Context());

        await act.Should().NotThrowAsync();
    }
}
