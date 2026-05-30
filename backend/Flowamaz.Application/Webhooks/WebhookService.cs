using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Entities.Webhooks;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Webhooks;

/// <summary>
/// Per-workflow inbound webhooks. Secrets are 32-byte CSPRNG values (64 hex chars), returned in
/// plaintext exactly once (create/rotate) and stored AES-256-GCM encrypted via
/// <see cref="ISecretProtector"/>. Payloads are authenticated with HMAC-SHA256 over the raw body,
/// compared in constant time. Triggering delegates to the orchestrator, which enforces the
/// published-version requirement and deduplicates on the idempotency key (exactly-once trigger).
/// </summary>
public sealed class WebhookService : IWebhookService
{
    private const int SecretByteCount = 32;
    private const int SyncTimeoutSeconds = 30;
    private const int SyncPollMs = 1000;

    private static readonly HashSet<InstanceStatus> TerminalStatuses =
        [InstanceStatus.Completed, InstanceStatus.Failed, InstanceStatus.Cancelled];

    private readonly IWebhookEndpointRepository _endpoints;
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly ISecretProtector _protector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        IWebhookEndpointRepository endpoints,
        IWorkflowDefinitionRepository definitions,
        IWorkflowInstanceRepository instances,
        IWorkflowOrchestrator orchestrator,
        ISecretProtector protector,
        IUnitOfWork unitOfWork,
        ILogger<WebhookService> logger)
    {
        _endpoints = endpoints;
        _definitions = definitions;
        _instances = instances;
        _orchestrator = orchestrator;
        _protector = protector;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CreatedWebhookResult> CreateEndpointAsync(
        Guid workspaceId, Guid workflowDefinitionId, string? description, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "WebhookService.CreateEndpointAsync enter workspace={WorkspaceId} workflow={WorkflowId}",
            workspaceId, workflowDefinitionId);
        try
        {
            _ = await _definitions.GetByIdForWorkspaceAsync(workflowDefinitionId, workspaceId, ct)
                ?? throw new InvalidOperationException(
                    $"Workflow '{workflowDefinitionId}' was not found in this workspace. " +
                    "Pick a workflow that belongs to this workspace.");

            var secret = GenerateSecret();
            var endpoint = new WebhookEndpoint
            {
                WorkspaceId = workspaceId,
                WorkflowDefinitionId = workflowDefinitionId,
                Description = description,
                EncryptedSecret = _protector.Protect(workspaceId, secret),
                IsActive = true,
            };

            await _endpoints.AddAsync(endpoint, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation(
                "WebhookService.CreateEndpointAsync exit workspace={WorkspaceId} endpoint={EndpointId}",
                workspaceId, endpoint.Id);
            return new CreatedWebhookResult(endpoint.Id, secret);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "WebhookService.CreateEndpointAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task<WebhookEndpointDto?> GetEndpointAsync(Guid endpointId, Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug("WebhookService.GetEndpointAsync enter workspace={WorkspaceId} endpoint={EndpointId}", workspaceId, endpointId);
        var endpoint = await _endpoints.GetByIdAsync(endpointId, workspaceId, ct);
        return endpoint is null ? null : ToDto(endpoint);
    }

    public async Task<IReadOnlyList<WebhookEndpointDto>> ListEndpointsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug("WebhookService.ListEndpointsAsync enter workspace={WorkspaceId}", workspaceId);
        var endpoints = await _endpoints.GetForWorkspaceAsync(workspaceId, ct);
        _logger.LogDebug("WebhookService.ListEndpointsAsync exit workspace={WorkspaceId} count={Count}", workspaceId, endpoints.Count);
        return endpoints.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<WebhookEndpointDto>> ListForWorkflowAsync(Guid workflowDefinitionId, Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "WebhookService.ListForWorkflowAsync enter workspace={WorkspaceId} workflow={WorkflowId}", workspaceId, workflowDefinitionId);
        var endpoints = await _endpoints.GetForWorkflowAsync(workflowDefinitionId, workspaceId, ct);
        return endpoints.Select(ToDto).ToList();
    }

    public async Task DeleteEndpointAsync(Guid endpointId, Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug("WebhookService.DeleteEndpointAsync enter workspace={WorkspaceId} endpoint={EndpointId}", workspaceId, endpointId);
        var endpoint = await _endpoints.GetByIdAsync(endpointId, workspaceId, ct)
            ?? throw new UnauthorizedAccessException(
                $"Webhook '{endpointId}' was not found in this workspace. It may already be deleted.");

        endpoint.IsDeleted = true;
        endpoint.IsActive = false;
        _endpoints.Update(endpoint);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("WebhookService.DeleteEndpointAsync exit workspace={WorkspaceId} endpoint={EndpointId}", workspaceId, endpointId);
    }

    public async Task<CreatedWebhookResult> RotateSecretAsync(Guid endpointId, Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug("WebhookService.RotateSecretAsync enter workspace={WorkspaceId} endpoint={EndpointId}", workspaceId, endpointId);
        var endpoint = await _endpoints.GetByIdAsync(endpointId, workspaceId, ct)
            ?? throw new UnauthorizedAccessException(
                $"Webhook '{endpointId}' was not found in this workspace.");

        var secret = GenerateSecret();
        endpoint.EncryptedSecret = _protector.Protect(workspaceId, secret);
        _endpoints.Update(endpoint);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("WebhookService.RotateSecretAsync exit workspace={WorkspaceId} endpoint={EndpointId}", workspaceId, endpointId);
        return new CreatedWebhookResult(endpoint.Id, secret);
    }

    public async Task<bool> ValidateSignatureAsync(Guid endpointId, string rawBody, string? signatureHeader, CancellationToken ct = default)
    {
        var endpoint = await _endpoints.FindByIdAsync(endpointId, ct);
        if (endpoint is null || !endpoint.IsActive)
        {
            _logger.LogWarning("WebhookService.ValidateSignatureAsync endpoint missing/inactive endpoint={EndpointId}", endpointId);
            return false;
        }

        var secret = _protector.Unprotect(endpoint.WorkspaceId, endpoint.EncryptedSecret);
        return VerifyHmac(secret, rawBody, signatureHeader);
    }

    public async Task<WebhookTriggerResult> TriggerFromWebhookAsync(
        Guid endpointId, string rawBody, string? signatureHeader, string? idempotencyKey,
        bool waitForTerminal = false, CancellationToken ct = default)
    {
        _logger.LogDebug("WebhookService.TriggerFromWebhookAsync enter endpoint={EndpointId}", endpointId);

        var endpoint = await _endpoints.FindByIdAsync(endpointId, ct);
        if (endpoint is null || !endpoint.IsActive)
            throw new UnauthorizedAccessException($"Webhook '{endpointId}' is not active or does not exist.");

        var secret = _protector.Unprotect(endpoint.WorkspaceId, endpoint.EncryptedSecret);
        if (!VerifyHmac(secret, rawBody, signatureHeader))
        {
            _logger.LogWarning("WebhookService.TriggerFromWebhookAsync invalid signature endpoint={EndpointId}", endpointId);
            throw new UnauthorizedAccessException(
                "Webhook signature verification failed. Sign the raw request body with HMAC-SHA256 " +
                "using your endpoint secret and send it in the X-Flowamaz-Signature header.");
        }

        var instance = await _orchestrator.TriggerAsync(
            endpoint.WorkspaceId, endpoint.WorkflowDefinitionId, rawBody, idempotencyKey,
            InstanceTriggerType.Webhook, correlationId: null, isTest: false, cancellationToken: ct);

        var status = instance.Status;
        var isTerminal = TerminalStatuses.Contains(status);

        if (waitForTerminal && !isTerminal)
        {
            (status, isTerminal) = await PollForTerminalAsync(instance.Id, ct);
        }

        _logger.LogInformation(
            "WebhookService.TriggerFromWebhookAsync exit endpoint={EndpointId} instance={InstanceId} status={Status}",
            endpointId, instance.Id, status);
        return new WebhookTriggerResult(instance.Id, status.ToString(), isTerminal);
    }

    private async Task<(InstanceStatus Status, bool IsTerminal)> PollForTerminalAsync(Guid instanceId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(SyncTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(SyncPollMs, ct);
            var current = await _instances.GetByIdAsync(instanceId, ct);
            if (current is not null && TerminalStatuses.Contains(current.Status))
                return (current.Status, true);
        }

        var last = await _instances.GetByIdAsync(instanceId, ct);
        return (last?.Status ?? InstanceStatus.Running, false);
    }

    private static string GenerateSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretByteCount)).ToLowerInvariant();

    private static bool VerifyHmac(string secret, string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var provided = signatureHeader.Trim();
        if (provided.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            provided = provided["sha256=".Length..];

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromHexString(provided);
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody));
        return CryptographicOperations.FixedTimeEquals(computed, providedBytes);
    }

    private static WebhookEndpointDto ToDto(WebhookEndpoint e) =>
        new(e.Id, e.WorkflowDefinitionId, e.Description, e.IsActive, e.AllowedIps, e.CreatedAt);
}
