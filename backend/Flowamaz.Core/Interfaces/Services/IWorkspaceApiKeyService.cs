using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Workspace API key management. Keys are stored as SHA256 hashes only; the plain value is
/// returned once (in <see cref="CreateApiKeyResponse.PlainKey"/>) and never again (FUNCTIONAL.md §12.5).
/// </summary>
public interface IWorkspaceApiKeyService
{
    Task<CreateApiKeyResponse> CreateApiKeyAsync(
        Guid workspaceId, Guid environmentId, string name, IReadOnlyList<string> scopes, Guid createdBy,
        DateTime? expiresAt = null, CancellationToken cancellationToken = default);

    /// <summary>Hashes the incoming plain key and looks it up; null when unknown, inactive or expired.</summary>
    Task<WorkspaceApiKeyValidationResult?> ValidateApiKeyAsync(string plainKey, CancellationToken cancellationToken = default);

    Task RevokeApiKeyAsync(Guid keyId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<List<ApiKeyDto>> GetApiKeysAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task RecordLastUsedAsync(Guid keyId, CancellationToken cancellationToken = default);
}
