using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Flowamaz.Application.Library.Services;

public sealed record ConnectorRatingResult(decimal AverageRating, int RatingCount);

public sealed record ConnectorReviewDto(Guid OrgId, int Rating, string? Review, DateTime CreatedAt);

public sealed record ConnectorSubmissionResult(Guid SubmissionId, string GithubPrUrl, string Status);

/// <summary>
/// Connector marketplace operations: ratings/reviews (one per org, upsert + average recompute) and
/// community connector submissions (validated manifest → GitHub PR → tracked submission row).
/// </summary>
public sealed class ConnectorMarketplaceService
{
    private const int MaxReviewLength = 500;

    private readonly IConnectorMarketplaceRepository _repo;
    private readonly IConnectorSubmissionPrService _prService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConnectorMarketplaceService> _logger;

    public ConnectorMarketplaceService(
        IConnectorMarketplaceRepository repo,
        IConnectorSubmissionPrService prService,
        IUnitOfWork unitOfWork,
        ILogger<ConnectorMarketplaceService> logger)
    {
        _repo = repo;
        _prService = prService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ConnectorRatingResult> RateConnectorAsync(
        string connectorId, Guid orgId, int rating, string? review, CancellationToken ct = default)
    {
        _logger.LogDebug("ConnectorMarketplaceService.RateConnectorAsync enter connector={ConnectorId} org={OrgId}", connectorId, orgId);

        if (rating is < 1 or > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5 stars.");
        if (review is { Length: > MaxReviewLength })
            throw new InvalidOperationException($"Review is too long. Keep it under {MaxReviewLength} characters.");

        var definition = await _repo.GetDefinitionByConnectorIdAsync(connectorId, ct)
            ?? throw new InvalidOperationException(
                $"Connector '{connectorId}' was not found in the catalogue. Refresh the library and try again.");

        var existing = await _repo.GetRatingAsync(definition.Id, orgId, ct);
        if (existing is not null)
        {
            existing.Rating = rating;
            existing.Review = review;
            _repo.UpdateRating(existing);
        }
        else
        {
            await _repo.AddRatingAsync(
                new ConnectorRating { ConnectorDefinitionId = definition.Id, OrgId = orgId, Rating = rating, Review = review }, ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);

        // Recompute the average from all persisted ratings.
        var ratings = await _repo.GetRatingsForConnectorAsync(definition.Id, ct);
        definition.RatingCount = ratings.Count;
        definition.AverageRating = ratings.Count == 0
            ? 0m
            : Math.Round((decimal)ratings.Average(r => r.Rating), 2);
        _repo.UpdateDefinition(definition);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ConnectorMarketplaceService.RateConnectorAsync exit connector={ConnectorId} avg={Avg} count={Count}",
            connectorId, definition.AverageRating, definition.RatingCount);
        return new ConnectorRatingResult(definition.AverageRating, definition.RatingCount);
    }

    public async Task<IReadOnlyList<ConnectorReviewDto>> GetReviewsAsync(string connectorId, CancellationToken ct = default)
    {
        var definition = await _repo.GetDefinitionByConnectorIdAsync(connectorId, ct);
        if (definition is null) return [];
        var ratings = await _repo.GetRatingsForConnectorAsync(definition.Id, ct);
        return ratings.Select(r => new ConnectorReviewDto(r.OrgId, r.Rating, r.Review, r.CreatedAt)).ToList();
    }

    public async Task<ConnectorSubmissionResult> SubmitConnectorAsync(
        Guid orgId, string connectorName, string manifestYaml, CancellationToken ct = default)
    {
        _logger.LogInformation("ConnectorMarketplaceService.SubmitConnectorAsync enter org={OrgId} name={Name}", orgId, connectorName);

        if (string.IsNullOrWhiteSpace(connectorName))
            throw new InvalidOperationException("A connector name is required.");
        ValidateManifest(manifestYaml);

        var prUrl = await _prService.CreatePullRequestAsync(connectorName, manifestYaml, orgId.ToString(), ct);

        var submission = new ConnectorSubmission
        {
            OrgId = orgId,
            ConnectorName = connectorName,
            ManifestYaml = manifestYaml,
            GithubPrUrl = prUrl,
            Status = "pending",
        };
        await _repo.AddSubmissionAsync(submission, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("ConnectorMarketplaceService.SubmitConnectorAsync exit submission={Id} pr={Pr}", submission.Id, prUrl);
        return new ConnectorSubmissionResult(submission.Id, prUrl, submission.Status);
    }

    private static void ValidateManifest(string manifestYaml)
    {
        if (string.IsNullOrWhiteSpace(manifestYaml))
            throw new InvalidOperationException("The connector manifest is empty. Upload a valid manifest YAML.");
        try
        {
            // Strict parse — rejects malformed YAML before opening a PR.
            new Deserializer().Deserialize<object>(manifestYaml);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The connector manifest is not valid YAML: {ex.Message}");
        }
    }
}
