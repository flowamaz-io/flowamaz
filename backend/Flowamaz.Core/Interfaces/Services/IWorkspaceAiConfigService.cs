using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Reads and updates a workspace's AI model configuration (FUNCTIONAL.md §5.3). The view shows the
/// resolved model per function and the level it came from; updates validate each override's
/// provider against the org allowlist and the model's capabilities before persisting.
/// </summary>
public interface IWorkspaceAiConfigService
{
    Task<AiConfigView> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the workspace function overrides. Throws <c>ConfigViolationException</c> when a
    /// provider is not in the org allowlist or the model fails its capability gate.
    /// </summary>
    Task UpdateAsync(Guid workspaceId, IReadOnlyList<FunctionOverrideInput> overrides, CancellationToken cancellationToken = default);
}
