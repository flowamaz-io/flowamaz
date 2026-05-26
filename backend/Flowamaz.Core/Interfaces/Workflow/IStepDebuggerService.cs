using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>
/// Step debugger for Dev/Staging instances (FUNCTIONAL.md §8.10). Every operation enforces, at the
/// service layer, that the instance is NOT in Production — a Production instance throws a 403
/// (<c>DebuggerNotAllowedException</c>). Returns null when the instance is not found in the workspace.
/// </summary>
public interface IStepDebuggerService
{
    Task<bool> PauseAsync(Guid workspaceId, Guid instanceId, string afterNodeId, CancellationToken cancellationToken = default);
    Task<DebugSnapshot?> InspectAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default);
    Task<bool> ForceVariableAsync(Guid workspaceId, Guid instanceId, string name, string value, CancellationToken cancellationToken = default);
    Task<bool> StepForwardAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default);
    Task<bool> ForceBranchAsync(Guid workspaceId, Guid instanceId, string routerNodeId, string targetBranchEdgeId, CancellationToken cancellationToken = default);
    Task<bool> ResumeAsync(Guid workspaceId, Guid instanceId, CancellationToken cancellationToken = default);
}
