using Flowamaz.Api.Authorization;
using Flowamaz.Application.Library.Services;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Connector marketplace: ratings/reviews (one per org) and community connector submissions.
/// Ratings are attributed to the caller's organisation, taken from the authenticated context.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}")]
public sealed class ConnectorMarketplaceController : ControllerBase
{
    private readonly ConnectorMarketplaceService _marketplace;
    private readonly ICurrentUserService _currentUser;

    public ConnectorMarketplaceController(ConnectorMarketplaceService marketplace, ICurrentUserService currentUser)
    {
        _marketplace = marketplace;
        _currentUser = currentUser;
    }

    [HttpGet("connectors/{connectorId}/reviews")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<ConnectorReviewDto>>> Reviews(string connectorId, CancellationToken ct) =>
        Ok(await _marketplace.GetReviewsAsync(connectorId, ct));

    [HttpPost("connectors/{connectorId}/rate")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<ConnectorRatingResult>> Rate(
        string connectorId, [FromBody] RateConnectorRequest request, CancellationToken ct)
    {
        var orgId = _currentUser.OrgId
            ?? throw new InvalidOperationException("An organisation context is required to rate a connector.");
        var result = await _marketplace.RateConnectorAsync(connectorId, orgId, request.Rating, request.Review, ct);
        return Ok(result);
    }

    [HttpPost("library/connectors/submit")]
    [RequireWorkspaceRole(WorkspaceRole.Designer)]
    public async Task<ActionResult<ConnectorSubmissionResult>> Submit(
        [FromBody] SubmitConnectorRequest request, CancellationToken ct)
    {
        var orgId = _currentUser.OrgId
            ?? throw new InvalidOperationException("An organisation context is required to submit a connector.");
        var result = await _marketplace.SubmitConnectorAsync(orgId, request.ConnectorName, request.ManifestYaml, ct);
        return Ok(result);
    }
}

public sealed record RateConnectorRequest(int Rating, string? Review);
public sealed record SubmitConnectorRequest(string ConnectorName, string ManifestYaml);
