using Flowamaz.Api.Authorization;
using Flowamaz.Application.Workspace.DTOs;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Workspace API key management — all Admin-gated. The plain key is returned exactly once on
/// create (with the <c>X-Plain-Key-One-Time</c> header); it is never present in the list response.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/api-keys")]
public sealed class WorkspaceApiKeysController : ControllerBase
{
    private readonly IWorkspaceApiKeyService _apiKeyService;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<CreateApiKeyRequest> _createValidator;

    public WorkspaceApiKeysController(
        IWorkspaceApiKeyService apiKeyService, ICurrentUserService currentUser, IValidator<CreateApiKeyRequest> createValidator)
    {
        _apiKeyService = apiKeyService;
        _currentUser = currentUser;
        _createValidator = createValidator;
    }

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<PagedResult<ApiKeyResponse>>> List(
        Guid workspaceId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var keys = await _apiKeyService.GetApiKeysAsync(workspaceId, cancellationToken);
        var items = keys
            // PlainKey is ALWAYS null in the list — the key is only ever shown once, at creation.
            .Select(k => new ApiKeyResponse(
                k.Id, k.EnvironmentId, k.Name, k.KeyPrefix, k.Scopes, k.LastUsedAt, k.ExpiresAt, k.IsActive, k.CreatedAt, PlainKey: null))
            .ToList();
        return Ok(PagedResult<ApiKeyResponse>.From(items, page, pageSize));
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<ApiKeyResponse>> Create(
        Guid workspaceId, [FromBody] CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var created = await _apiKeyService.CreateApiKeyAsync(
            workspaceId, request.EnvironmentId, request.Name, request.Scopes, _currentUser.UserId!.Value, request.ExpiresAt, cancellationToken);

        Response.Headers["X-Plain-Key-One-Time"] = "true";
        var key = created.ApiKey;
        return Ok(new ApiKeyResponse(
            key.Id, key.EnvironmentId, key.Name, key.KeyPrefix, key.Scopes, key.LastUsedAt, key.ExpiresAt, key.IsActive, key.CreatedAt,
            PlainKey: created.PlainKey));
    }

    [HttpDelete("{keyId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> Revoke(Guid workspaceId, Guid keyId, CancellationToken cancellationToken)
    {
        await _apiKeyService.RevokeApiKeyAsync(keyId, workspaceId, cancellationToken);
        return NoContent();
    }
}
