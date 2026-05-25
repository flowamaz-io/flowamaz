namespace Flowamaz.Core.Models;

/// <summary>
/// Read model for a workspace API key. Exposes the non-sensitive <see cref="KeyPrefix"/> only —
/// never the plain key or its hash (FUNCTIONAL.md §12.5).
/// </summary>
public sealed record ApiKeyDto(
    Guid Id,
    Guid WorkspaceId,
    Guid EnvironmentId,
    string Name,
    string KeyPrefix,
    IReadOnlyList<string> Scopes,
    DateTime? LastUsedAt,
    DateTime? ExpiresAt,
    bool IsActive,
    DateTime CreatedAt);
