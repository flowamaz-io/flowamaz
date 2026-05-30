using System.Collections.Concurrent;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workflow.Debugger;

/// <summary>
/// Thread-safe, process-lifetime, in-memory breakpoint store (prompt 07-04). Registered as a
/// singleton. Nothing here touches the database — breakpoints vanish on restart, which is the
/// intended behaviour for a Development-only debugging aid.
/// </summary>
public sealed class BreakpointRegistry : IBreakpointRegistry
{
    private readonly ConcurrentDictionary<Guid, WorkflowBreakpoint> _breakpoints = new();
    // (instanceId, nodeId) pairs already stepped past, so a resumed breakpoint does not re-pause.
    private readonly ConcurrentDictionary<(Guid InstanceId, string NodeId), byte> _resumed = new();
    private readonly ILogger<BreakpointRegistry> _logger;

    public BreakpointRegistry(ILogger<BreakpointRegistry> logger) => _logger = logger;

    public WorkflowBreakpoint Add(Guid workspaceId, Guid workflowId, string nodeId)
    {
        // De-dupe: a breakpoint on the same (workspace, workflow, node) is reused.
        var existing = _breakpoints.Values.FirstOrDefault(
            b => b.WorkspaceId == workspaceId && b.WorkflowId == workflowId &&
                 string.Equals(b.NodeId, nodeId, StringComparison.Ordinal));
        if (existing is not null) return existing;

        var bp = new WorkflowBreakpoint(Guid.NewGuid(), workspaceId, workflowId, nodeId);
        _breakpoints[bp.Id] = bp;
        _logger.LogInformation(
            "BreakpointRegistry.Add workspace={WorkspaceId} workflow={WorkflowId} node={NodeId} id={BreakpointId}",
            workspaceId, workflowId, nodeId, bp.Id);
        return bp;
    }

    public bool Remove(Guid id)
    {
        var removed = _breakpoints.TryRemove(id, out _);
        _logger.LogInformation("BreakpointRegistry.Remove id={BreakpointId} removed={Removed}", id, removed);
        return removed;
    }

    public IReadOnlyList<WorkflowBreakpoint> List(Guid workspaceId) =>
        _breakpoints.Values.Where(b => b.WorkspaceId == workspaceId).ToList();

    public bool IsBreakpointSet(Guid workspaceId, Guid workflowId, string nodeId) =>
        _breakpoints.Values.Any(
            b => b.WorkspaceId == workspaceId && b.WorkflowId == workflowId &&
                 string.Equals(b.NodeId, nodeId, StringComparison.Ordinal));

    public bool ShouldPause(Guid workspaceId, Guid workflowId, Guid instanceId, string nodeId) =>
        IsBreakpointSet(workspaceId, workflowId, nodeId) &&
        !_resumed.ContainsKey((instanceId, nodeId));

    public void MarkResumed(Guid instanceId, string nodeId)
    {
        _resumed[(instanceId, nodeId)] = 1;
        _logger.LogInformation(
            "BreakpointRegistry.MarkResumed instance={InstanceId} node={NodeId}", instanceId, nodeId);
    }
}
