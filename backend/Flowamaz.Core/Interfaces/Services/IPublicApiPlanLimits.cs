namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// The per-minute public API limit that applies to a workspace's organisation (FUNCTIONAL.md §13.2).
/// <see cref="PerMinute"/> 0 means no per-minute limit (Enterprise without an agreed figure).
/// <see cref="IsCustom"/>: the figure was agreed for this organisation (it overrides the plan), so messages
/// must not quote the plan's figure or suggest upgrading.
/// </summary>
public sealed record PlanApiLimit(Guid OrgId, string PlanName, int PerMinute, bool IsCustom = false);

public interface IPublicApiPlanLimits
{
    /// <summary>Null if the workspace or its organisation can't be found.</summary>
    Task<PlanApiLimit?> ResolveAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
