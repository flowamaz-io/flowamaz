using Flowamaz.Core.Entities.Library;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Persistence for workflow templates (official + community). Templates are global, not
/// workspace-scoped, so reads are public; writes (publish) attribute the publishing org.</summary>
public interface IWorkflowTemplateRepository
{
    /// <summary>Active, approved templates for the gallery, optionally filtered by category/search.</summary>
    Task<List<WorkflowTemplate>> ListAsync(string? category, string? search, CancellationToken cancellationToken = default);

    Task<WorkflowTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    Task AddAsync(WorkflowTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Atomically increments install_count at the DB level (no read-modify-write race).</summary>
    Task IncrementInstallCountAsync(Guid id, CancellationToken cancellationToken = default);
}
