using Flowamaz.Core.Enums;
using Flowamaz.Core.Models;

namespace Flowamaz.Application.Workspace.DTOs;

// ── Requests ────────────────────────────────────────────────────────────────

public sealed record CreateWorkspaceRequest(string Name, string Slug);

public sealed record UpdateWorkspaceSettingsRequest(
    int MaxConcurrentRuns,
    int RunRetentionDays,
    decimal AiCostBudgetMonthUsd,
    List<string> AllowedAiProviders,
    Dictionary<string, string> DefaultAiModelOverrides,
    MarketplacePolicy MarketplacePolicy);

public sealed record AddMemberRequest(string Email, WorkspaceRole Role);

public sealed record UpdateRoleRequest(WorkspaceRole Role);

public sealed record CreateApiKeyRequest(string Name, Guid EnvironmentId, List<string> Scopes, DateTime? ExpiresAt);

public sealed record UpdateAiConfigRequest(List<FunctionOverrideRequest> Overrides);

public sealed record FunctionOverrideRequest(string FunctionId, string Provider, string ModelId, string KeySource);

// ── Responses ───────────────────────────────────────────────────────────────

public sealed record WorkspaceResponse(Guid Id, Guid OrgId, string Name, string Slug, WorkspaceSettings Settings, DateTime CreatedAt);

public sealed record WorkspaceListItem(Guid Id, string Name, string Slug, string Role);

public sealed record MemberResponse(Guid OrgUserId, string Email, string Name, WorkspaceRole Role, DateTime JoinedAt, bool IsActive);

/// <summary>API key view. <see cref="PlainKey"/> is non-null ONLY on the create response (shown once).</summary>
public sealed record ApiKeyResponse(
    Guid Id,
    Guid EnvironmentId,
    string Name,
    string KeyPrefix,
    IReadOnlyList<string> Scopes,
    DateTime? LastUsedAt,
    DateTime? ExpiresAt,
    bool IsActive,
    DateTime CreatedAt,
    string? PlainKey);

// ── Pagination ──────────────────────────────────────────────────────────────

public sealed record PaginationMeta(int Page, int PageSize, int Total, int TotalPages);

public sealed record PagedResult<T>(IReadOnlyList<T> Data, PaginationMeta Pagination)
{
    public static PagedResult<T> From(IReadOnlyList<T> all, int page, int pageSize)
    {
        var clampedSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var clampedPage = page <= 0 ? 1 : page;
        var total = all.Count;
        var totalPages = (int)Math.Ceiling(total / (double)clampedSize);
        var items = all.Skip((clampedPage - 1) * clampedSize).Take(clampedSize).ToList();
        return new PagedResult<T>(items, new PaginationMeta(clampedPage, clampedSize, total, totalPages));
    }
}
