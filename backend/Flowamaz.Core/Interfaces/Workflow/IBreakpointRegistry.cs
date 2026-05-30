using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>
/// In-memory registry of development breakpoints (prompt 07-04). Breakpoints are intentionally NOT
/// persisted — they live for the lifetime of the process and are only consulted in the Development
/// environment. The registry itself does not enforce the environment gate; callers do.
/// </summary>
public interface IBreakpointRegistry
{
    WorkflowBreakpoint Add(Guid workspaceId, Guid workflowId, string nodeId);
    bool Remove(Guid id);
    IReadOnlyList<WorkflowBreakpoint> List(Guid workspaceId);

    /// <summary>True if a breakpoint is set for this workflow + node in the workspace.</summary>
    bool IsBreakpointSet(Guid workspaceId, Guid workflowId, string nodeId);

    /// <summary>
    /// True when the orchestrator should pause this instance before <paramref name="nodeId"/>: a
    /// breakpoint is set AND this (instance, node) has not already been resumed past. Resuming marks
    /// the pair via <see cref="MarkResumed"/> so the same breakpoint does not re-trigger immediately.
    /// </summary>
    bool ShouldPause(Guid workspaceId, Guid workflowId, Guid instanceId, string nodeId);

    /// <summary>Records that this instance has been resumed past <paramref name="nodeId"/>'s breakpoint.</summary>
    void MarkResumed(Guid instanceId, string nodeId);
}
