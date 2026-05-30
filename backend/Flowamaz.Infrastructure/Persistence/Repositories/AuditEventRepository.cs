using System.Runtime.CompilerServices;
using Flowamaz.Core.Entities.Audit;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read access to the append-only audit_events table plus the retention DELETE. Every query is
/// org-scoped (and workspace-scoped when a workspace id is supplied) — never returns events from a
/// different organisation (workspace isolation).
/// </summary>
public sealed class AuditEventRepository(FlowAmazDbContext db) : IAuditEventRepository
{
    public async Task<(IReadOnlyList<AuditEvent> Items, int Total)> QueryAsync(
        AuditQueryFilter filter, CancellationToken ct = default)
    {
        var query = BuildQuery(filter);
        var total = await query.CountAsync(ct);

        var page = filter.Page <= 0 ? 1 : filter.Page;
        var size = Math.Clamp(filter.PageSize <= 0 ? 20 : filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return (items, total);
    }

    public async IAsyncEnumerable<AuditEvent> StreamAsync(
        AuditQueryFilter filter, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = BuildQuery(filter).OrderByDescending(e => e.CreatedAt).AsAsyncEnumerable();
        await foreach (var e in query.WithCancellation(ct))
        {
            yield return e;
        }
    }

    public async Task<int> DeleteOlderThanAsync(Guid orgId, DateTime cutoffUtc, CancellationToken ct = default) =>
        await db.AuditEvents
            .Where(e => e.OrgId == orgId && e.CreatedAt < cutoffUtc)
            .ExecuteDeleteAsync(ct);

    private IQueryable<AuditEvent> BuildQuery(AuditQueryFilter filter)
    {
        var query = db.AuditEvents.AsNoTracking()
            .Where(e => e.OrgId == filter.OrgId)
            .Where(e => e.CreatedAt >= filter.From && e.CreatedAt <= filter.To);

        if (filter.WorkspaceId is { } workspaceId)
            query = query.Where(e => e.WorkspaceId == workspaceId);

        if (!string.IsNullOrWhiteSpace(filter.EventType))
            query = query.Where(e => e.EventType == filter.EventType);

        if (filter.ActorUserId is { } actorId)
            query = query.Where(e => e.ActorUserId == actorId);

        if (!string.IsNullOrWhiteSpace(filter.ResourceType))
            query = query.Where(e => e.ResourceType == filter.ResourceType);

        return query;
    }
}
