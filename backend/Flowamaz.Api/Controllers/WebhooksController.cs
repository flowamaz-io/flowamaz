using Flowamaz.Api.Authorization;
using Flowamaz.Application.Webhooks.DTOs;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Webhook endpoint management — Admin-gated create/delete/rotate, Viewer-gated list. The HMAC
/// secret is returned exactly once (create and rotate) and is never present in list/get responses.
/// </summary>
[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly IWebhookService _webhooks;
    private readonly IValidator<CreateWebhookRequest> _createValidator;

    public WebhooksController(IWebhookService webhooks, IValidator<CreateWebhookRequest> createValidator)
    {
        _webhooks = webhooks;
        _createValidator = createValidator;
    }

    /// <summary>Lists the workspace's webhook endpoints, optionally filtered to a single workflow. Requires Viewer role; HMAC secrets are never returned.</summary>
    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    public async Task<ActionResult<IReadOnlyList<WebhookEndpointDto>>> List(
        Guid workspaceId, [FromQuery] Guid? workflowId, CancellationToken cancellationToken)
    {
        var endpoints = workflowId is { } wf
            ? await _webhooks.ListForWorkflowAsync(wf, workspaceId, cancellationToken)
            : await _webhooks.ListEndpointsAsync(workspaceId, cancellationToken);
        return Ok(endpoints);
    }

    /// <summary>Creates an HMAC-signed inbound webhook endpoint that triggers the given workflow. Requires Admin role; the signing secret is returned exactly once in this response.</summary>
    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<CreatedWebhookResult>> Create(
        Guid workspaceId, [FromBody] CreateWebhookRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _webhooks.CreateEndpointAsync(
            workspaceId, request.WorkflowDefinitionId, request.Description, cancellationToken);
        return Ok(result);
    }

    /// <summary>Deletes a webhook endpoint so it no longer accepts inbound calls. Requires Admin role.</summary>
    [HttpDelete("{endpointId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid endpointId, CancellationToken cancellationToken)
    {
        await _webhooks.DeleteEndpointAsync(endpointId, workspaceId, cancellationToken);
        return NoContent();
    }

    /// <summary>Rotates the endpoint's HMAC signing secret, invalidating the previous one. Requires Admin role; the new secret is returned exactly once in this response.</summary>
    [HttpPost("{endpointId:guid}/rotate")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<CreatedWebhookResult>> Rotate(
        Guid workspaceId, Guid endpointId, CancellationToken cancellationToken)
    {
        var result = await _webhooks.RotateSecretAsync(endpointId, workspaceId, cancellationToken);
        return Ok(result);
    }
}
