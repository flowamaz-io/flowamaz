using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Walks the 5-level model resolution hierarchy from FUNCTIONAL.md §5.3
/// (node → workflow → workspace → org → platform) and returns the first defined config.
/// Enforces capability gates (F3 needs vision; F7 needs ≥100k ctx) — throws
/// <c>ConfigViolationException</c> immediately, never at call time.
/// </summary>
public interface IModelResolutionService
{
    /// <summary>
    /// Resolve the model config for a given function in the context of a workspace.
    /// Throws <c>ConfigViolationException</c> if the resolved model fails capability checks.
    /// Throws <c>InvalidOperationException</c> only for unknown function/model IDs (programmer error).
    /// </summary>
    Task<ModelConfig> ResolveModelConfigAsync(
        string functionId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
}
