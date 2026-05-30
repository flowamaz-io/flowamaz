using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Workspace.Services;

/// <summary>
/// Workspace API key management. The plain key is generated with a CSPRNG, returned exactly once,
/// and only its SHA256 hash is persisted. The plain key is never logged and cannot be recovered
/// (FUNCTIONAL.md §12.5). Key format: <c>fmz_{live|test}_{slug8}_{random32hex}</c> (FUNCTIONAL.md §4.7).
/// </summary>
public sealed class WorkspaceApiKeyService : IWorkspaceApiKeyService
{
    private const int RandomByteCount = 16; // → 32 hex chars
    private const int SlugFragmentLength = 8;
    private const int PrefixLength = 16;

    private readonly IWorkspaceApiKeyRepository _apiKeyRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WorkspaceApiKeyService> _logger;
    private readonly IAuditService? _audit;

    public WorkspaceApiKeyService(
        IWorkspaceApiKeyRepository apiKeyRepository,
        IWorkspaceRepository workspaceRepository,
        IUnitOfWork unitOfWork,
        ILogger<WorkspaceApiKeyService> logger,
        IAuditService? audit = null)
    {
        _apiKeyRepository = apiKeyRepository;
        _workspaceRepository = workspaceRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _audit = audit;
    }

    public async Task<CreateApiKeyResponse> CreateApiKeyAsync(
        Guid workspaceId, Guid environmentId, string name, IReadOnlyList<string> scopes, Guid createdBy,
        DateTime? expiresAt = null, CancellationToken cancellationToken = default)
    {
        // Never log the plain key, the hash, or the prefix beyond what is non-sensitive.
        _logger.LogDebug(
            "WorkspaceApiKeyService.CreateApiKeyAsync enter workspaceId={WorkspaceId} environmentId={EnvironmentId} createdBy={CreatedBy} scopeCount={ScopeCount}",
            workspaceId, environmentId, createdBy, scopes.Count);
        try
        {
            var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken)
                ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");

            var environment = await _workspaceRepository.GetEnvironmentByIdAsync(environmentId, workspaceId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Environment '{environmentId}' does not belong to workspace '{workspaceId}'.");

            var invalidScopes = scopes.Where(s => !ApiKeyScope.IsValid(s)).ToList();
            if (invalidScopes.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Unknown API key scope(s): {string.Join(", ", invalidScopes)}. " +
                    $"Valid scopes are: {string.Join(", ", ApiKeyScope.All)}.");
            }

            var plainKey = GeneratePlainKey(workspace.Slug, environment.Name);
            var apiKey = new WorkspaceApiKey
            {
                WorkspaceId = workspaceId,
                EnvironmentId = environmentId,
                Name = name,
                KeyHash = HashKey(plainKey),
                KeyPrefix = plainKey[..Math.Min(PrefixLength, plainKey.Length)],
                Scopes = scopes.ToList(),
                CreatedBy = createdBy,
                ExpiresAt = expiresAt,
                IsActive = true,
            };

            await _apiKeyRepository.AddAsync(apiKey, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Audit: record metadata only — NEVER the plain key or its hash.
            _audit?.RecordAsync(new AuditEventRequest
            {
                WorkspaceId = workspaceId,
                ActorUserId = createdBy,
                ActorType = "user",
                EventType = "api_key.created",
                ResourceType = "api_key",
                ResourceId = apiKey.Id,
                ResourceLabel = name,
                Action = "created",
                Metadata = new { prefix = apiKey.KeyPrefix, scopes },
            }, cancellationToken);

            _logger.LogInformation(
                "WorkspaceApiKeyService.CreateApiKeyAsync exit workspaceId={WorkspaceId} keyId={KeyId} prefix={Prefix}",
                workspaceId, apiKey.Id, apiKey.KeyPrefix);
            return new CreateApiKeyResponse { ApiKey = apiKey, PlainKey = plainKey };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceApiKeyService.CreateApiKeyAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task<WorkspaceApiKeyValidationResult?> ValidateApiKeyAsync(string plainKey, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceApiKeyService.ValidateApiKeyAsync enter");
        try
        {
            if (string.IsNullOrWhiteSpace(plainKey))
            {
                _logger.LogDebug("WorkspaceApiKeyService.ValidateApiKeyAsync exit valid=false reason=empty");
                return null;
            }

            var key = await _apiKeyRepository.GetByHashAsync(HashKey(plainKey), cancellationToken);
            var isUsable = key is { IsActive: true } && (key.ExpiresAt is null || key.ExpiresAt > DateTime.UtcNow);
            if (!isUsable)
            {
                _logger.LogDebug(
                    "WorkspaceApiKeyService.ValidateApiKeyAsync exit valid=false keyFound={Found}", key is not null);
                return null;
            }

            _logger.LogDebug(
                "WorkspaceApiKeyService.ValidateApiKeyAsync exit valid=true keyId={KeyId} workspaceId={WorkspaceId}",
                key!.Id, key.WorkspaceId);
            return new WorkspaceApiKeyValidationResult(key.Id, key.WorkspaceId, key.EnvironmentId, key.Scopes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceApiKeyService.ValidateApiKeyAsync error");
            throw;
        }
    }

    public async Task RevokeApiKeyAsync(Guid keyId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceApiKeyService.RevokeApiKeyAsync enter keyId={KeyId} workspaceId={WorkspaceId}", keyId, workspaceId);
        try
        {
            var key = await _apiKeyRepository.GetByIdAsync(keyId, workspaceId, cancellationToken);
            if (key is null)
            {
                _logger.LogWarning(
                    "WorkspaceApiKeyService.RevokeApiKeyAsync no-op keyId={KeyId} workspaceId={WorkspaceId} — not found in workspace",
                    keyId, workspaceId);
                return;
            }

            key.IsActive = false;
            _apiKeyRepository.Update(key);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _audit?.RecordAsync(new AuditEventRequest
            {
                WorkspaceId = workspaceId,
                ActorType = "user",
                EventType = "api_key.revoked",
                ResourceType = "api_key",
                ResourceId = key.Id,
                ResourceLabel = key.Name,
                Action = "revoked",
            }, cancellationToken);

            _logger.LogInformation("WorkspaceApiKeyService.RevokeApiKeyAsync exit keyId={KeyId} workspaceId={WorkspaceId}", keyId, workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceApiKeyService.RevokeApiKeyAsync error keyId={KeyId} workspaceId={WorkspaceId}", keyId, workspaceId);
            throw;
        }
    }

    public async Task<List<ApiKeyDto>> GetApiKeysAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceApiKeyService.GetApiKeysAsync enter workspaceId={WorkspaceId}", workspaceId);
        try
        {
            var keys = await _apiKeyRepository.GetForWorkspaceAsync(workspaceId, cancellationToken);
            var dtos = keys
                .Select(k => new ApiKeyDto(
                    k.Id, k.WorkspaceId, k.EnvironmentId, k.Name, k.KeyPrefix, k.Scopes,
                    k.LastUsedAt, k.ExpiresAt, k.IsActive, k.CreatedAt))
                .ToList();
            _logger.LogDebug("WorkspaceApiKeyService.GetApiKeysAsync exit workspaceId={WorkspaceId} count={Count}", workspaceId, dtos.Count);
            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceApiKeyService.GetApiKeysAsync error workspaceId={WorkspaceId}", workspaceId);
            throw;
        }
    }

    public async Task RecordLastUsedAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WorkspaceApiKeyService.RecordLastUsedAsync enter keyId={KeyId}", keyId);
        try
        {
            var key = await _apiKeyRepository.FindByIdAsync(keyId, cancellationToken);
            if (key is null)
            {
                _logger.LogWarning("WorkspaceApiKeyService.RecordLastUsedAsync no-op keyId={KeyId} — not found", keyId);
                return;
            }

            key.LastUsedAt = DateTime.UtcNow;
            _apiKeyRepository.Update(key);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("WorkspaceApiKeyService.RecordLastUsedAsync exit keyId={KeyId}", keyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkspaceApiKeyService.RecordLastUsedAsync error keyId={KeyId}", keyId);
            throw;
        }
    }

    private static string GeneratePlainKey(string workspaceSlug, WorkspaceEnvironmentType environment)
    {
        var env = environment == WorkspaceEnvironmentType.Production ? "live" : "test";
        var slugFragment = new string((workspaceSlug ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Take(SlugFragmentLength)
            .ToArray())
            .ToLowerInvariant();
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(RandomByteCount)).ToLowerInvariant();
        return $"fmz_{env}_{slugFragment}_{random}";
    }

    private static string HashKey(string plainKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainKey))).ToLowerInvariant();
}
