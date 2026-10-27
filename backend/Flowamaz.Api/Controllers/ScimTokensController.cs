using Flowamaz.Api.Authorization;
using Flowamaz.Application.Auth.Scim;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// SCIM tokens for the caller's own organisation (org owners, Enterprise). A token is shown once, when created.
/// </summary>
[ApiController]
public sealed class ScimTokensController : ControllerBase
{
    private readonly ScimService _scim;
    private readonly ICurrentUserService _currentUser;

    public ScimTokensController(ScimService scim, ICurrentUserService currentUser)
    {
        _scim = scim;
        _currentUser = currentUser;
    }

    public sealed record CreateScimTokenRequest(string? Name);

    [HttpGet("/api/v1/organisations/{id:guid}/scim")]
    [RequireOrgOwner]
    public async Task<IActionResult> List(Guid id, CancellationToken ct)
    {
        if (OtherOrg(id) is { } notFound) return notFound;
        return Ok(new { scimBaseUrl = _scim.ScimBaseUrl, tokens = await _scim.ListTokensAsync(id, ct) });
    }

    [HttpPost("/api/v1/organisations/{id:guid}/scim/tokens")]
    [RequireOrgOwner]
    public async Task<IActionResult> Create(Guid id, [FromBody] CreateScimTokenRequest? request, CancellationToken ct)
    {
        if (OtherOrg(id) is { } notFound) return notFound;
        var created = await _scim.CreateTokenAsync(id, request?.Name, _currentUser.UserId ?? Guid.Empty, ct);
        return Ok(new { token = created.Token, plainToken = created.PlainToken, scimBaseUrl = created.ScimBaseUrl });
    }

    [HttpDelete("/api/v1/organisations/{id:guid}/scim/tokens/{tokenId:guid}")]
    [RequireOrgOwner]
    public async Task<IActionResult> Revoke(Guid id, Guid tokenId, CancellationToken ct)
    {
        if (OtherOrg(id) is { } notFound) return notFound;
        await _scim.RevokeTokenAsync(id, tokenId, _currentUser.UserId ?? Guid.Empty, ct);
        return NoContent();
    }

    /// <summary>Only the caller's own organisation; 404 otherwise so organisation ids can't be probed.</summary>
    private ObjectResult? OtherOrg(Guid id) =>
        _currentUser.OrgId == id
            ? null
            : NotFound(new
            {
                success = false,
                statusCode = 404,
                code = "ORG_NOT_FOUND",
                message = "No organisation with that id is available to you. Open SSO settings from your own organisation.",
            });
}
