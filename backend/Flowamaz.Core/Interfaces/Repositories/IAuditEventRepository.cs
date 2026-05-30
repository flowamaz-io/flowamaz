using Flowamaz.Core.Entities.Audit;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Filter for querying audit events. Null members are not applied.</summary>
public sealed class AuditQueryFilter
{
    /// <summary>Required for workspace-scoped queries; null for org-wide queries.</summary>
    public Guid? WorkspaceId { get; init; }

    /// <summary>Required: every query is scoped to a single organisation (isolation).</summary>
    public Guid OrgId { get; init; }

    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public string? EventType { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ResourceType { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>Read access to the append-only audit_events table. Writes go via IAuditService.</summary>
public interface IAuditEventRepository
{
    /// <summary>Paged, filtered audit events newest-first. Returns the page items and total count.</summary>
    Task<(IReadOnlyList<AuditEvent> Items, int Total)> QueryAsync(AuditQueryFilter filter, CancellationToken ct = default);

    /// <summary>Streams matching events newest-first for CSV export (no paging). Honours the filter range.</summary>
    IAsyncEnumerable<AuditEvent> StreamAsync(AuditQueryFilter filter, CancellationToken ct = default);

    /// <summary>Deletes events for an org older than the cutoff. Used ONLY by the retention job. Returns rows deleted.</summary>
    Task<int> DeleteOlderThanAsync(Guid orgId, DateTime cutoffUtc, CancellationToken ct = default);
}
