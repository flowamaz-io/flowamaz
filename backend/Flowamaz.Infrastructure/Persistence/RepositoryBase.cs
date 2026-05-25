using System.Linq.Expressions;
using Flowamaz.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence;

/// <summary>
/// Generic repository for entities. Workspace isolation is layered on by
/// <see cref="WorkspaceRepositoryBase{T}"/> — that subclass is the only path used for
/// workspace-scoped data. Both honour the soft-delete query filter applied in DbContext.
/// </summary>
public class RepositoryBase<T> where T : BaseEntity
{
    protected readonly FlowAmazDbContext Db;
    protected DbSet<T> Set => Db.Set<T>();

    public RepositoryBase(FlowAmazDbContext db) { Db = db; }

    // FirstOrDefaultAsync (not FindAsync) so the soft-delete query filter applied in DbContext
    // is honoured — FindAsync hits the change tracker / SQL bypassing global query filters,
    // which would silently return soft-deleted rows.
    public virtual Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public virtual IQueryable<T> Query() => Set.AsQueryable();

    public virtual async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        await Set.AddAsync(entity, ct);
        return entity;
    }

    public virtual void Update(T entity) => Set.Update(entity);

    public virtual void SoftDelete(T entity)
    {
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        Set.Update(entity);
    }
}

/// <summary>
/// Repository for workspace-scoped entities. Every query MUST go through one of the
/// workspace-aware methods; raw access to <see cref="DbContext"/> from controllers bypasses
/// this guard and is forbidden by CLAUDE.md §1 (workspace isolation).
/// </summary>
public class WorkspaceRepositoryBase<T> : RepositoryBase<T> where T : WorkspaceEntity
{
    public WorkspaceRepositoryBase(FlowAmazDbContext db) : base(db) { }

    /// <summary>Filtered query — workspace_id is always applied; never expose <see cref="Query"/>.</summary>
    public IQueryable<T> QueryForWorkspace(Guid workspaceId) =>
        Set.Where(e => e.WorkspaceId == workspaceId);

    public async Task<T?> GetByIdForWorkspaceAsync(Guid id, Guid workspaceId, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(e => e.Id == id && e.WorkspaceId == workspaceId, ct);

    public Task<bool> ExistsInWorkspaceAsync(
        Expression<Func<T, bool>> predicate, Guid workspaceId, CancellationToken ct = default) =>
        Set.AnyAsync(predicate.AndAlso(e => e.WorkspaceId == workspaceId), ct);
}

internal static class ExpressionExtensions
{
    public static Expression<Func<T, bool>> AndAlso<T>(
        this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(T));
        var leftBody = new ReplaceParameterVisitor(left.Parameters[0], parameter).Visit(left.Body)!;
        var rightBody = new ReplaceParameterVisitor(right.Parameters[0], parameter).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(leftBody, rightBody), parameter);
    }

    private sealed class ReplaceParameterVisitor(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }
}
