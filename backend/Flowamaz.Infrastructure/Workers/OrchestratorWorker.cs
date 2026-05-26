using System.Text.Json;
using Flowamaz.Core.Interfaces.Queue;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Workers;

/// <summary>
/// Background worker that drains the per-workspace queues and drives instances through the
/// orchestrator. Concurrency is bounded by a <see cref="SemaphoreSlim"/> (Worker:Concurrency,
/// default 4). Each claimed instance binds a 30s DB lease that a side task renews every 10s.
/// Exceptions never escape — a BackgroundService that throws would take down the host.
/// </summary>
public sealed class OrchestratorWorker : BackgroundService
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseRenewInterval = TimeSpan.FromSeconds(10);

    private readonly ITaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrchestratorWorker> _logger;
    private readonly string _workerId = $"worker-{Guid.NewGuid():N}";
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly int _concurrency;

    public OrchestratorWorker(
        ITaskQueue queue,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OrchestratorWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _concurrency = Math.Max(1, configuration.GetValue<int?>("Worker:Concurrency") ?? 4);
        _concurrencyGate = new SemaphoreSlim(_concurrency, _concurrency);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OrchestratorWorker {WorkerId} started — concurrency={Concurrency}", _workerId, _concurrency);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = false;
                var workspaces = await _queue.GetActiveWorkspacesAsync(stoppingToken);

                foreach (var workspaceId in workspaces)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    var instanceId = await _queue.DequeueAsync(workspaceId, _workerId, stoppingToken);
                    if (instanceId is null) continue;

                    await _concurrencyGate.WaitAsync(stoppingToken);
                    dispatched = true;
                    _ = Task.Run(() => ProcessWithLeaseAsync(workspaceId, instanceId.Value, stoppingToken), stoppingToken);
                }

                if (!dispatched) await Task.Delay(PollDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrchestratorWorker {WorkerId} loop error — backing off", _workerId);
                await SafeDelayAsync(ErrorDelay, stoppingToken);
            }
        }

        _logger.LogInformation("OrchestratorWorker {WorkerId} stopping", _workerId);
    }

    private async Task ProcessWithLeaseAsync(Guid workspaceId, Guid instanceId, CancellationToken stoppingToken)
    {
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewal = RenewLeaseLoopAsync(instanceId, renewalCts.Token);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await ProcessInstanceAsync(scope.ServiceProvider, workspaceId, instanceId, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "OrchestratorWorker {WorkerId} failed processing instance={InstanceId}", _workerId, instanceId);
        }
        finally
        {
            await renewalCts.CancelAsync();
            try { await renewal; } catch (OperationCanceledException) { }
            _concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Claims the instance's DB lease, advances it one step, then executes any returned Action/AI
    /// nodes via the <see cref="INodeWorkerRegistry"/>. Completing a node re-queues the instance
    /// (orchestrator), so each queue message is one step+execute cycle until the run ends or hits a
    /// gate. Acknowledges + releases the lease at the end regardless of outcome.
    /// </summary>
    internal async Task ProcessInstanceAsync(
        IServiceProvider services,
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        var orchestrator = services.GetRequiredService<IWorkflowOrchestrator>();
        var instances = services.GetRequiredService<IWorkflowInstanceRepository>();

        var acquired = await instances.TryAcquireLeaseAsync(instanceId, _workerId, cancellationToken);
        if (!acquired)
        {
            _logger.LogWarning(
                "OrchestratorWorker {WorkerId} could not lease instance={InstanceId} — another worker owns it", _workerId, instanceId);
            await _queue.AcknowledgeAsync(workspaceId, instanceId, cancellationToken);
            return;
        }

        try
        {
            var result = await orchestrator.StepAsync(instanceId, _workerId, cancellationToken);
            _logger.LogInformation(
                "OrchestratorWorker {WorkerId} stepped instance={InstanceId} next={Next} complete={Complete} gate={Gate}",
                _workerId, instanceId, result.NextNodes.Count, result.IsComplete, result.NeedsHumanGate);

            if (!result.IsComplete && !result.NeedsHumanGate && result.NextNodes.Count > 0)
            {
                await ExecuteNodesAsync(services, orchestrator, workspaceId, instanceId, result.NextNodes, cancellationToken);
            }
        }
        finally
        {
            await _queue.AcknowledgeAsync(workspaceId, instanceId, cancellationToken);
            await instances.ReleaseLeaseAsync(instanceId, _workerId, cancellationToken);
        }
    }

    private async Task ExecuteNodesAsync(
        IServiceProvider services,
        IWorkflowOrchestrator orchestrator,
        Guid workspaceId,
        Guid instanceId,
        IReadOnlyList<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var instances = services.GetRequiredService<IWorkflowInstanceRepository>();
        var versions = services.GetRequiredService<IWorkflowVersionRepository>();
        var parser = services.GetRequiredService<SfgParser>();
        var registry = services.GetRequiredService<INodeWorkerRegistry>();

        var instance = await instances.GetByIdAsync(instanceId, cancellationToken);
        if (instance is null) return;
        var version = await versions.GetByIdForWorkspaceAsync(instance.WorkflowVersionId, workspaceId, cancellationToken);
        if (version is null) return;
        var graph = await parser.ParseAsync(version.YamlContent, cancellationToken);
        var emptyVariables = new Dictionary<string, JsonElement>();

        foreach (var nodeId in nodeIds)
        {
            var node = graph.FindNode(nodeId);
            if (node is null) continue;
            var nodeWorker = registry.Resolve(node.Type);
            if (nodeWorker is null) continue; // no executor for this type yet — leave it Pending

            try
            {
                var context = new NodeExecutionContext(instanceId, workspaceId, node, graph, emptyVariables, _workerId);
                var result = await nodeWorker.ExecuteAsync(context, cancellationToken);
                if (result.Success)
                {
                    var output = result.Output?.RootElement.GetRawText() ?? "{}";
                    await orchestrator.CompleteNodeAsync(instanceId, nodeId, output, _workerId, cancellationToken);
                }
                else
                {
                    await orchestrator.FailNodeAsync(instanceId, nodeId, result.ErrorMessage ?? "Node execution failed.", _workerId, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "OrchestratorWorker {WorkerId} node execution threw node={NodeId}", _workerId, nodeId);
                await orchestrator.FailNodeAsync(instanceId, nodeId, $"Node executor error: {ex.Message}", _workerId, cancellationToken);
            }
        }
    }

    private async Task RenewLeaseLoopAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(LeaseRenewInterval, cancellationToken);
                using var scope = _scopeFactory.CreateScope();
                var instances = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
                await instances.RenewLeaseAsync(instanceId, _workerId, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when processing finishes — stop renewing.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OrchestratorWorker {WorkerId} lease renewal error instance={InstanceId}", _workerId, instanceId);
        }
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }
}
