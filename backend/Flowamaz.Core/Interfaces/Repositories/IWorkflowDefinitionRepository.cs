using Flowamaz.Core.Entities.Workflow;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for workflow definitions. Add/Update only stage; the caller commits via
/// <see cref="Persistence.IUnitOfWork"/>. Every read is workspace-scoped (CLAUDE.md §1).
/// </summary>
public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<List<WorkflowDefinition>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Resolves a workflow by its workspace-unique slug — used by the public API.</summary>
    Task<WorkflowDefinition?> GetBySlugForWorkspaceAsync(Guid workspaceId, string slug, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsInWorkspaceAsync(Guid workspaceId, string slug, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
    void Update(WorkflowDefinition definition);
}
