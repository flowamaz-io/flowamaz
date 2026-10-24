using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>
/// Detects missing essentials in an NL workflow description (approver, trigger, rejection path,
/// systems) and returns up to 3 clarifying questions. Deterministic heuristics first; an AI call
/// (metered, via model resolution) only when the heuristics find nothing yet the description is thin.
/// </summary>
public interface INlClarificationService
{
    Task<ClarificationResult> GetQuestionsAsync(NlWorkflowRequest request, Guid workspaceId, CancellationToken cancellationToken = default);
}
