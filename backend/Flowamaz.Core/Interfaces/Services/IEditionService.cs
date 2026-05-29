using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Edition-aware limit enforcement (prompt 05-07). Community edition enforces hard config caps;
/// cloud editions defer to the org's Plan limits. Used by service methods that create billable
/// resources (workflows, runs, members).
/// </summary>
public interface IEditionService
{
    string Edition { get; }
    bool IsCommunity { get; }

    /// <summary>The caps for the current edition (community → config; cloud → unlimited sentinel here).</summary>
    Task<EditionLimits> GetEditionLimitsAsync(CancellationToken ct = default);

    /// <summary>Evaluates whether the workspace is at/over the given limit right now.</summary>
    Task<LimitCheckResult> CheckLimitAsync(Guid workspaceId, LimitType limitType, CancellationToken ct = default);

    /// <summary>Throws <see cref="Exceptions.EditionLimitException"/> if creating one more would exceed the limit.</summary>
    Task EnsureWithinLimitAsync(Guid workspaceId, LimitType limitType, CancellationToken ct = default);
}
