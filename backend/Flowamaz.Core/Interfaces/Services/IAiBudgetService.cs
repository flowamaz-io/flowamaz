namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Checks and updates the per-workspace AI token budget (FUNCTIONAL.md §5.5 lever 8).
/// Hard cap enforced at request time — caller should gate AI calls behind IsBudgetAvailableAsync.
/// </summary>
public interface IAiBudgetService
{
    /// <summary>
    /// Returns true if the workspace has AI budget remaining. Returns true if no budget row exists
    /// (unconfigured workspaces are unrestricted).
    /// </summary>
    Task<bool> IsBudgetAvailableAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
