namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>
/// Replays a terminal workflow instance as a fresh TEST run (prompt 07-04). The new instance pins
/// the same workflow version, merges the original trigger payload with an optional override, and
/// records its lineage via <c>ParentInstanceId</c>. Replay never creates a production run.
/// </summary>
public interface IReplayService
{
    /// <summary>
    /// Creates a new test instance from <paramref name="instanceId"/>. Throws
    /// <c>InstanceNotReplayableException</c> (409) when the source is not terminal
    /// (Completed/Failed/Cancelled). Returns the new instance id, or null if the source instance is
    /// not found in the workspace.
    /// </summary>
    Task<Guid?> ReplayInstanceAsync(
        Guid instanceId, string? payloadOverride, Guid workspaceId, CancellationToken cancellationToken = default);
}
