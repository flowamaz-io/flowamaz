using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface IWorkflowDnaService
{
    Task<WorkflowDna> ComputeDnaAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowSimilarity>> FindSimilarAsync(Guid workspaceId, WorkflowDna dna, int limit, CancellationToken cancellationToken = default);
    Task<CloneSuggestion?> SuggestCloneAsync(Guid workspaceId, string partialName, CancellationToken cancellationToken = default);
}
