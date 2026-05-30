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

    [HttpDelete("{endpointId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid endpointId, CancellationToken cancellationToken)
    {
        await _webhooks.DeleteEndpointAsync(endpointId, workspaceId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{endpointId:guid}/rotate")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    public async Task<ActionResult<CreatedWebhookResult>> Rotate(
        Guid workspaceId, Guid endpointId, CancellationToken cancellationToken)
    {
        var result = await _webhooks.RotateSecretAsync(endpointId, workspaceId, cancellationToken);
        return Ok(result);
    }
}
