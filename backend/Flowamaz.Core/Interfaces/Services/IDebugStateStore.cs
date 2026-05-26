namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Stores transient step-debugger state (pause points, single-step tokens, forced branches) in
/// Redis with a 1-hour TTL so debug sessions expire on their own. The Application layer depends on
/// this abstraction; the Redis implementation lives in Infrastructure.
/// </summary>
public interface IDebugStateStore
{
    Task SetPauseAfterAsync(Guid instanceId, string nodeId, CancellationToken cancellationToken = default);
    Task<string?> GetPauseAfterAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Grants one single-step advance for a paused instance.</summary>
    Task GrantStepAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Consumes a pending single-step grant; true if one was present.</summary>
    Task<bool> ConsumeStepAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task SetForcedBranchAsync(Guid instanceId, string routerNodeId, string edgeId, CancellationToken cancellationToken = default);
    Task<string?> GetForcedBranchAsync(Guid instanceId, string routerNodeId, CancellationToken cancellationToken = default);

    /// <summary>Clears all debug state for the instance (resume normal execution).</summary>
    Task ClearAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
