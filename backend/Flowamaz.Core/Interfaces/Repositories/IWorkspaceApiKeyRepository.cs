using Flowamaz.Core.Entities.Workspaces;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for workspace API keys. Lookups by id are workspace-scoped; lookup by hash is
/// global (the hash is the credential, validated before a workspace context exists).
/// </summary>
public interface IWorkspaceApiKeyRepository
{
    Task<WorkspaceApiKey?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Global lookup by key id (no workspace scope) — for last-used bookkeeping on an already-validated key.</summary>
    Task<WorkspaceApiKey?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkspaceApiKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken = default);
    Task<List<WorkspaceApiKey>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task AddAsync(WorkspaceApiKey apiKey, CancellationToken cancellationToken = default);
    void Update(WorkspaceApiKey apiKey);
}
