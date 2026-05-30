using Flowamaz.Core.Entities.Webhooks;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for webhook endpoints. Id lookups are workspace-scoped; <see cref="FindByIdAsync"/>
/// is global for the anonymous receive endpoint, which has no workspace context until the row is loaded.
/// </summary>
public sealed class WebhookEndpointRepository(FlowAmazDbContext db) : IWebhookEndpointRepository
{
    public Task<WebhookEndpoint?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == id && w.WorkspaceId == workspaceId, cancellationToken);

    public Task<WebhookEndpoint?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<List<WebhookEndpoint>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WebhookEndpoints.AsNoTracking()
            .Where(w => w.WorkspaceId == workspaceId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<List<WebhookEndpoint>> GetForWorkflowAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.WebhookEndpoints.AsNoTracking()
            .Where(w => w.WorkflowDefinitionId == workflowDefinitionId && w.WorkspaceId == workspaceId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken = default) =>
        await db.WebhookEndpoints.AddAsync(endpoint, cancellationToken);

    public void Update(WebhookEndpoint endpoint) => db.WebhookEndpoints.Update(endpoint);
}
