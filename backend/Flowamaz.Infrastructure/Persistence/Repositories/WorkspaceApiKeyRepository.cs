using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for workspace API keys. Id lookups are workspace-scoped; the hash lookup is global
/// because validation happens before any workspace context exists. Reads that precede a mutation
/// (revoke, last-used bookkeeping) are tracked.
/// </summary>
public sealed class WorkspaceApiKeyRepository(FlowAmazDbContext db) : IWorkspaceApiKeyRepository
{
    public Task<WorkspaceApiKey?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkspaceApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.WorkspaceId == workspaceId, cancellationToken);

    public Task<WorkspaceApiKey?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WorkspaceApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    public Task<WorkspaceApiKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken = default) =>
        db.WorkspaceApiKeys.FirstOrDefaultAsync(k => k.KeyHash == keyHash, cancellationToken);

    public Task<List<WorkspaceApiKey>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WorkspaceApiKeys.AsNoTracking().Where(k => k.WorkspaceId == workspaceId).ToListAsync(cancellationToken);

    public async Task AddAsync(WorkspaceApiKey apiKey, CancellationToken cancellationToken = default) =>
        await db.WorkspaceApiKeys.AddAsync(apiKey, cancellationToken);

    public void Update(WorkspaceApiKey apiKey) => db.WorkspaceApiKeys.Update(apiKey);
}
