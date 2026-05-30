namespace Flowamaz.Application.Webhooks.DTOs;

/// <summary>Request to create a webhook endpoint for a published workflow.</summary>
public sealed record CreateWebhookRequest(Guid WorkflowDefinitionId, string? Description);
