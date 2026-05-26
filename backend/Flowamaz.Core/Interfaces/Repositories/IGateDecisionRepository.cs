using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for human approval gates. Add/Update only stage; the caller commits via
/// <see cref="Persistence.IUnitOfWork"/>. <see cref="GetExpiredAsync"/> backs the timer scanner.
/// </summary>
public interface IGateDecisionRepository
{
    Task<GateDecision?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-workspace lookup by gate ID only — used exclusively for signed email/Slack
    /// click-through flows where the workspace is unknown until the gate entity is loaded.
    /// Callers must verify the HMAC signature before trusting the returned entity.
    /// </summary>
    Task<GateDecision?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GateDecision?> GetByNodeAsync(Guid instanceId, string nodeId, CancellationToken cancellationToken = default);
    Task<List<GateDecision>> GetForInstanceAsync(Guid instanceId, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Pending gates across a workspace — backs the portal approval queue.</summary>
    Task<List<GateDecision>> GetPendingForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Pending gates whose <c>ExpiresAt</c> is at or before <paramref name="asOf"/> (timer scanner).</summary>
    Task<List<GateDecision>> GetExpiredAsync(DateTime asOf, CancellationToken cancellationToken = default);
    Task AddAsync(GateDecision gate, CancellationToken cancellationToken = default);
    void Update(GateDecision gate);
}
