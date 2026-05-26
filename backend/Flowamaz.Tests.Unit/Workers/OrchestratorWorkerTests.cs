using System.Text.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Flowamaz.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workers;

/// <summary>
/// OrchestratorWorker (fix-02-01): drives the per-step lease/claim/execute cycle with a mocked
/// service scope. Covers the internal <c>ProcessInstanceAsync</c> directly and the
/// <c>BackgroundService</c> loop via Start/Stop so dispatch + acknowledge are exercised without a
/// real Redis/Postgres/AI dependency.
/// </summary>
public class OrchestratorWorkerTests
{
    private const string Yaml = """
        workflow: { id: w, version: v1, name: W }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    private readonly Mock<ITaskQueue> _queue = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IWorkflowOrchestrator> _orchestrator = new();
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<INodeWorkerRegistry> _registry = new();
    private readonly SfgParser _parser = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();

    private IServiceProvider BuildProvider()
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IWorkflowOrchestrator))).Returns(_orchestrator.Object);
        provider.Setup(p => p.GetService(typeof(IWorkflowInstanceRepository))).Returns(_instances.Object);
        provider.Setup(p => p.GetService(typeof(IWorkflowVersionRepository))).Returns(_versions.Object);
        provider.Setup(p => p.GetService(typeof(SfgParser))).Returns(_parser);
        provider.Setup(p => p.GetService(typeof(INodeWorkerRegistry))).Returns(_registry.Object);
        return provider.Object;
    }

    private OrchestratorWorker NewWorker(int concurrency = 4)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Worker:Concurrency"] = concurrency.ToString() })
            .Build();
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(BuildProvider());
        _scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return new OrchestratorWorker(_queue.Object, _scopeFactory.Object, config, NullLogger<OrchestratorWorker>.Instance);
    }

    private void SetupRunnableInstance()
    {
        _instances.Setup(r => r.GetByIdAsync(_instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance { Id = _instanceId, WorkspaceId = _ws, WorkflowVersionId = _versionId });
        _versions.Setup(r => r.GetByIdForWorkspaceAsync(_versionId, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowVersion { Id = _versionId, WorkspaceId = _ws, YamlContent = Yaml });
    }

    [Fact]
    public async Task ProcessInstance_LeaseNotAcquired_AcknowledgesAndDoesNotStep()
    {
        _instances.Setup(r => r.TryAcquireLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var worker = NewWorker();

        await worker.ProcessInstanceAsync(BuildProvider(), _ws, _instanceId, CancellationToken.None);

        _queue.Verify(q => q.AcknowledgeAsync(_ws, _instanceId, It.IsAny<CancellationToken>()), Times.Once);
        _orchestrator.Verify(o => o.StepAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessInstance_StepComplete_AcknowledgesAndReleasesLease()
    {
        _instances.Setup(r => r.TryAcquireLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orchestrator.Setup(o => o.StepAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrchestratorResult.Complete);
        var worker = NewWorker();

        await worker.ProcessInstanceAsync(BuildProvider(), _ws, _instanceId, CancellationToken.None);

        _queue.Verify(q => q.AcknowledgeAsync(_ws, _instanceId, It.IsAny<CancellationToken>()), Times.Once);
        _instances.Verify(r => r.ReleaseLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(r => r.Resolve(It.IsAny<NodeType>()), Times.Never);
    }

    [Fact]
    public async Task ProcessInstance_NeedsHumanGate_AcknowledgesAndDoesNotExecuteNodes()
    {
        _instances.Setup(r => r.TryAcquireLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orchestrator.Setup(o => o.StepAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrchestratorResult.Gate(["gate"]));
        var worker = NewWorker();

        await worker.ProcessInstanceAsync(BuildProvider(), _ws, _instanceId, CancellationToken.None);

        _queue.Verify(q => q.AcknowledgeAsync(_ws, _instanceId, It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(r => r.Resolve(It.IsAny<NodeType>()), Times.Never);
    }

    [Fact]
    public async Task ProcessInstance_ContinueWithNode_WorkerSucceeds_CompletesNode()
    {
        _instances.Setup(r => r.TryAcquireLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orchestrator.Setup(o => o.StepAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrchestratorResult.Continue(["a"]));
        SetupRunnableInstance();
        var nodeWorker = new Mock<INodeWorker>();
        nodeWorker.Setup(w => w.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NodeExecutionResult.Ok(JsonDocument.Parse("""{"ok":true}""")));
        _registry.Setup(r => r.Resolve(NodeType.Action)).Returns(nodeWorker.Object);
        var worker = NewWorker();

        await worker.ProcessInstanceAsync(BuildProvider(), _ws, _instanceId, CancellationToken.None);

        _orchestrator.Verify(o => o.CompleteNodeAsync(_instanceId, "a", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessInstance_ContinueWithNode_WorkerFails_FailsNode()
    {
        _instances.Setup(r => r.TryAcquireLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orchestrator.Setup(o => o.StepAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrchestratorResult.Continue(["a"]));
        SetupRunnableInstance();
        var nodeWorker = new Mock<INodeWorker>();
        nodeWorker.Setup(w => w.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NodeExecutionResult.Fatal("boom"));
        _registry.Setup(r => r.Resolve(NodeType.Action)).Returns(nodeWorker.Object);
        var worker = NewWorker();

        await worker.ProcessInstanceAsync(BuildProvider(), _ws, _instanceId, CancellationToken.None);

        _orchestrator.Verify(o => o.FailNodeAsync(_instanceId, "a", "boom", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _orchestrator.Verify(o => o.CompleteNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessInstance_ContinueWithNode_WorkerThrows_FailsNodeAndDoesNotPropagate()
    {
        _instances.Setup(r => r.TryAcquireLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orchestrator.Setup(o => o.StepAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrchestratorResult.Continue(["a"]));
        SetupRunnableInstance();
        var nodeWorker = new Mock<INodeWorker>();
        nodeWorker.Setup(w => w.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kaboom"));
        _registry.Setup(r => r.Resolve(NodeType.Action)).Returns(nodeWorker.Object);
        var worker = NewWorker();

        var act = async () => await worker.ProcessInstanceAsync(BuildProvider(), _ws, _instanceId, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _orchestrator.Verify(o => o.FailNodeAsync(_instanceId, "a", It.Is<string>(s => s.Contains("kaboom")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessInstance_ContinueWithNode_NoExecutorRegistered_SkipsNode()
    {
        _instances.Setup(r => r.TryAcquireLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orchestrator.Setup(o => o.StepAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrchestratorResult.Continue(["a"]));
        SetupRunnableInstance();
        _registry.Setup(r => r.Resolve(It.IsAny<NodeType>())).Returns((INodeWorker?)null);
        var worker = NewWorker();

        await worker.ProcessInstanceAsync(BuildProvider(), _ws, _instanceId, CancellationToken.None);

        _orchestrator.Verify(o => o.CompleteNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _orchestrator.Verify(o => o.FailNodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DequeuesInstance_DispatchesAndAcknowledges()
    {
        var done = new ManualResetEventSlim(false);
        _queue.Setup(q => q.GetActiveWorkspacesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_ws]);
        var dequeued = 0;
        _queue.Setup(q => q.DequeueAsync(_ws, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref dequeued) == 1 ? _instanceId : (Guid?)null);
        _instances.Setup(r => r.TryAcquireLeaseAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orchestrator.Setup(o => o.StepAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrchestratorResult.Complete);
        _queue.Setup(q => q.AcknowledgeAsync(_ws, _instanceId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask).Callback(() => done.Set());

        var worker = NewWorker();
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        done.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        _orchestrator.Verify(o => o.StepAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_NoActiveWorkspaces_PollsWithoutDispatching()
    {
        var polled = new ManualResetEventSlim(false);
        _queue.Setup(q => q.GetActiveWorkspacesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]).Callback(() => polled.Set());

        var worker = NewWorker();
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        polled.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        _queue.Verify(q => q.DequeueAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_QueueThrows_BacksOffWithoutCrashing()
    {
        var threw = new ManualResetEventSlim(false);
        _queue.Setup(q => q.GetActiveWorkspacesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => threw.Set())
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var worker = NewWorker();
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        threw.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await cts.CancelAsync();
        var act = async () => await worker.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
