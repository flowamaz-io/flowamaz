using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Entities.Library;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Persistence for connector marketplace metrics, ratings, and community submissions.</summary>
public interface IConnectorMarketplaceRepository
{
    Task<ConnectorDefinition?> GetDefinitionByConnectorIdAsync(string connectorId, CancellationToken cancellationToken = default);
    Task<ConnectorDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken cancellationToken = default);
    void UpdateDefinition(ConnectorDefinition definition);

    Task<ConnectorRating?> GetRatingAsync(Guid connectorDefinitionId, Guid orgId, CancellationToken cancellationToken = default);
    Task<List<ConnectorRating>> GetRatingsForConnectorAsync(Guid connectorDefinitionId, CancellationToken cancellationToken = default);
    Task AddRatingAsync(ConnectorRating rating, CancellationToken cancellationToken = default);
    void UpdateRating(ConnectorRating rating);

    Task AddSubmissionAsync(ConnectorSubmission submission, CancellationToken cancellationToken = default);
}
