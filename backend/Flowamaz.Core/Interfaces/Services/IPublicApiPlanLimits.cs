namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// The per-minute public API limit that applies to a workspace's organisation (FUNCTIONAL.md §13.2).
/// <see cref="PerMinute"/> 0 means no per-minute limit (Enterprise without an agreed figure).
/// </summary>
public sealed record PlanApiLimit(Guid OrgId, string PlanName, int PerMinute);

public interface IPublicApiPlanLimits
{
    /// <summary>Null if the workspace or its organisation can't be found.</summary>
    Task<PlanApiLimit?> ResolveAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
