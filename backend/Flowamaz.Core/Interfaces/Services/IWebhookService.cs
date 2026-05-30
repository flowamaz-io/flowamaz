namespace Flowamaz.Core.Interfaces.Services;

/// <summary>Webhook endpoint as returned to management UIs — never carries the secret.</summary>
public sealed record WebhookEndpointDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    string? Description,
    bool IsActive,
    IReadOnlyList<string> AllowedIps,
    DateTime CreatedAt);

/// <summary>Returned once when an endpoint is created or its secret rotated. The plain secret is shown only here.</summary>
public sealed record CreatedWebhookResult(Guid EndpointId, string Secret);

/// <summary>Outcome of triggering a workflow from an inbound webhook.</summary>
public sealed record WebhookTriggerResult(Guid InstanceId, string Status, bool IsTerminal);

/// <summary>
/// Per-workflow inbound webhooks: lifecycle management plus signature verification and trigger
/// dispatch for the public receive endpoint. All management operations are workspace-scoped.
/// </summary>
public interface IWebhookService
{
    Task<CreatedWebhookResult> CreateEndpointAsync(Guid workspaceId, Guid workflowDefinitionId, string? description, CancellationToken ct = default);

    Task<WebhookEndpointDto?> GetEndpointAsync(Guid endpointId, Guid workspaceId, CancellationToken ct = default);

    Task<IReadOnlyList<WebhookEndpointDto>> ListEndpointsAsync(Guid workspaceId, CancellationToken ct = default);

    Task<IReadOnlyList<WebhookEndpointDto>> ListForWorkflowAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken ct = default);

    Task DeleteEndpointAsync(Guid endpointId, Guid workspaceId, CancellationToken ct = default);

    Task<CreatedWebhookResult> RotateSecretAsync(Guid endpointId, Guid workspaceId, CancellationToken ct = default);

    /// <summary>Constant-time HMAC-SHA256 verification of <paramref name="rawBody"/> against the endpoint secret.</summary>
    Task<bool> ValidateSignatureAsync(Guid endpointId, string rawBody, string? signatureHeader, CancellationToken ct = default);

    /// <summary>
    /// Verifies the signature, ensures the workflow is published, and triggers an instance.
    /// Deduplicates on <paramref name="idempotencyKey"/> (the orchestrator returns the existing
    /// instance for a repeated key). When <paramref name="waitForTerminal"/> is set, polls up to
    /// 30s for the instance to reach a terminal state (sync mode).
    /// Throws <see cref="UnauthorizedAccessException"/> on a missing/invalid signature.
    /// </summary>
    Task<WebhookTriggerResult> TriggerFromWebhookAsync(
        Guid endpointId,
        string rawBody,
        string? signatureHeader,
        string? idempotencyKey,
        bool waitForTerminal = false,
        CancellationToken ct = default);
}
