using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface INlYamlGenerationService
{
    Task<GenerationResult> GenerateAsync(NlWorkflowRequest request, Guid workspaceId, CancellationToken cancellationToken = default);
}
