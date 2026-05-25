using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Guards the instance state machine (prompt 02-01). Illegal transitions must be impossible so a
/// durable execution can never jump from, say, Completed back to Running.
/// </summary>
public class WorkflowInstanceTests
{
    [Theory]
    [InlineData(InstanceStatus.Pending, InstanceStatus.Running)]
    [InlineData(InstanceStatus.Pending, InstanceStatus.Cancelled)]
    [InlineData(InstanceStatus.Running, InstanceStatus.Waiting)]
    [InlineData(InstanceStatus.Running, InstanceStatus.Completed)]
    [InlineData(InstanceStatus.Running, InstanceStatus.Failed)]
    [InlineData(InstanceStatus.Running, InstanceStatus.Compensating)]
    [InlineData(InstanceStatus.Waiting, InstanceStatus.Running)]
    [InlineData(InstanceStatus.Failed, InstanceStatus.Compensating)]
    [InlineData(InstanceStatus.Compensating, InstanceStatus.Completed)]
    public void CanTransitionTo_allows_valid_transitions(InstanceStatus from, InstanceStatus to)
    {
        var instance = new WorkflowInstance { Status = from };
        instance.CanTransitionTo(to).Should().BeTrue();
        instance.TransitionTo(to);
        instance.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(InstanceStatus.Pending, InstanceStatus.Completed)]
    [InlineData(InstanceStatus.Pending, InstanceStatus.Waiting)]
    [InlineData(InstanceStatus.Completed, InstanceStatus.Running)]
    [InlineData(InstanceStatus.Cancelled, InstanceStatus.Running)]
    [InlineData(InstanceStatus.Failed, InstanceStatus.Completed)]
    [InlineData(InstanceStatus.Running, InstanceStatus.Pending)]
    public void CanTransitionTo_rejects_invalid_transitions(InstanceStatus from, InstanceStatus to)
    {
        var instance = new WorkflowInstance { Status = from };
        instance.CanTransitionTo(to).Should().BeFalse();
    }

    [Fact]
    public void TransitionTo_throws_on_illegal_transition_and_keeps_status()
    {
        var instance = new WorkflowInstance { Status = InstanceStatus.Completed };
        var act = () => instance.TransitionTo(InstanceStatus.Running);
        act.Should().Throw<InvalidOperationException>();
        instance.Status.Should().Be(InstanceStatus.Completed);
    }

    [Theory]
    [InlineData(InstanceStatus.Completed)]
    [InlineData(InstanceStatus.Cancelled)]
    public void Terminal_states_reject_all_transitions(InstanceStatus terminal)
    {
        var instance = new WorkflowInstance { Status = terminal };
        foreach (var target in Enum.GetValues<InstanceStatus>())
        {
            instance.CanTransitionTo(target).Should().BeFalse();
        }
    }
}
