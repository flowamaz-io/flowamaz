using FluentAssertions;
using Flowamaz.Application.Workflow.Debugger;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Step debugger (prompt 02-05): a pause point is stored for a Dev instance, and every operation on
/// a Production instance is refused with 403 (the gate lives in the service, not just the controller).
/// </summary>
public class StepDebuggerServiceTests
{
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowNodeStateRepository> _nodeStates = new();
    private readonly Mock<IWorkflowVariableRepository> _variables = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IDebugStateStore> _debugState = new();
    private readonly Mock<ITaskQueue> _queue = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();

    private StepDebuggerService NewService() => new(
        _instances.Object, _nodeStates.Object, _variables.Object, _events.Object, _versions.Object,
        _debugState.Object, _queue.Object, _unitOfWork.Object, new SfgParser(), NullLogger<StepDebuggerService>.Instance);

    private void SetupInstance(WorkspaceEnvironmentType env) =>
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(_instanceId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance { Id = _instanceId, WorkspaceId = _ws, EnvironmentType = env, Status = InstanceStatus.Running });

    [Fact]
    public async Task Pause_stores_pause_point_for_dev_instance()
    {
        SetupInstance(WorkspaceEnvironmentType.Dev);

        var ok = await NewService().PauseAsync(_ws, _instanceId, "submit-request");

        ok.Should().BeTrue();
        _debugState.Verify(s => s.SetPauseAfterAsync(_instanceId, "submit-request", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Production_instance_is_forbidden_on_every_operation()
    {
        SetupInstance(WorkspaceEnvironmentType.Production);
        var service = NewService();

        await FluentActions.Awaiting(() => service.PauseAsync(_ws, _instanceId, "n")).Should().ThrowAsync<DebuggerNotAllowedException>();
        await FluentActions.Awaiting(() => service.InspectAsync(_ws, _instanceId)).Should().ThrowAsync<DebuggerNotAllowedException>();
        await FluentActions.Awaiting(() => service.ForceVariableAsync(_ws, _instanceId, "a", "1")).Should().ThrowAsync<DebuggerNotAllowedException>();
        await FluentActions.Awaiting(() => service.StepForwardAsync(_ws, _instanceId)).Should().ThrowAsync<DebuggerNotAllowedException>();
        await FluentActions.Awaiting(() => service.ForceBranchAsync(_ws, _instanceId, "r", "e1")).Should().ThrowAsync<DebuggerNotAllowedException>();
        await FluentActions.Awaiting(() => service.ResumeAsync(_ws, _instanceId)).Should().ThrowAsync<DebuggerNotAllowedException>();

        _debugState.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unknown_instance_returns_false()
    {
        // No setup → repo returns null.
        (await NewService().PauseAsync(_ws, _instanceId, "n")).Should().BeFalse();
    }
}
