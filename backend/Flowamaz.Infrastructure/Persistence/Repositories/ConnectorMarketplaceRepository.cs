using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for connector marketplace metrics, ratings, and submissions.</summary>
public sealed class ConnectorMarketplaceRepository(FlowAmazDbContext db) : IConnectorMarketplaceRepository
{
    public Task<ConnectorDefinition?> GetDefinitionByConnectorIdAsync(string connectorId, CancellationToken cancellationToken = default) =>
        db.ConnectorDefinitions.FirstOrDefaultAsync(c => c.ConnectorId == connectorId && !c.IsDeleted, cancellationToken);

    public Task<ConnectorDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.ConnectorDefinitions.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);

    public void UpdateDefinition(ConnectorDefinition definition) => db.ConnectorDefinitions.Update(definition);

    public Task<ConnectorRating?> GetRatingAsync(Guid connectorDefinitionId, Guid orgId, CancellationToken cancellationToken = default) =>
        db.ConnectorRatings.FirstOrDefaultAsync(
            r => r.ConnectorDefinitionId == connectorDefinitionId && r.OrgId == orgId && !r.IsDeleted, cancellationToken);

    public Task<List<ConnectorRating>> GetRatingsForConnectorAsync(Guid connectorDefinitionId, CancellationToken cancellationToken = default) =>
        db.ConnectorRatings.AsNoTracking()
            .Where(r => r.ConnectorDefinitionId == connectorDefinitionId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddRatingAsync(ConnectorRating rating, CancellationToken cancellationToken = default) =>
        await db.ConnectorRatings.AddAsync(rating, cancellationToken);

    public void UpdateRating(ConnectorRating rating) => db.ConnectorRatings.Update(rating);

    public async Task AddSubmissionAsync(ConnectorSubmission submission, CancellationToken cancellationToken = default) =>
        await db.ConnectorSubmissions.AddAsync(submission, cancellationToken);
}
