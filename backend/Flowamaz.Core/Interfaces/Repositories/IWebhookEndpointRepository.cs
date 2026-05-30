using Flowamaz.Core.Entities.Webhooks;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for webhook endpoints. Management lookups are workspace-scoped; the public
/// receive path resolves an endpoint by id alone (no workspace context exists yet) and then
/// uses the row's <see cref="WebhookEndpoint.WorkspaceId"/> to scope every follow-on operation.
/// </summary>
public interface IWebhookEndpointRepository
{
    Task<WebhookEndpoint?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Global lookup by endpoint id — for the anonymous receive endpoint only.</summary>
    Task<WebhookEndpoint?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<WebhookEndpoint>> GetForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<List<WebhookEndpoint>> GetForWorkflowAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken cancellationToken = default);

    Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken = default);
    void Update(WebhookEndpoint endpoint);
}
