using Flowamaz.Core.Enums;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>
/// Generates audience-specific narratives of a workflow run (FUNCTIONAL.md §8.10). Only the CEO
/// audience calls AI (F5, metered); Auditor and Developer are deterministic from the event log and
/// node states (zero AI cost).
/// </summary>
public interface IWorkflowInterpreterService
{
    /// <summary>Returns null when the instance is not found in the workspace.</summary>
    Task<InterpreterNarrative?> GenerateNarrativeAsync(
        Guid workspaceId, Guid instanceId, NarrativeAudience audience, CancellationToken cancellationToken = default);
}
