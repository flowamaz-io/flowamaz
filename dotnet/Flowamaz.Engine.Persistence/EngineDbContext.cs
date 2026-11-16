using System.Linq.Expressions;
using Flowamaz.Core.Entities;
using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Flowamaz.Infrastructure.Persistence;

/// <summary>
/// The workflow runtime's storage: the 7 run entities (definitions, versions, instances, node states,
/// variables, the event log and gate decisions) plus the conventions every Flowamaz table follows:
/// snake_case names for tables, columns, keys and indexes; a soft-delete query filter on every
/// <see cref="BaseEntity"/>; and CreatedAt/UpdatedAt (plus CreatedBy/UpdatedBy on
/// <see cref="AuditableEntity"/>) stamped on save.
/// <para>
/// A standalone host uses this context directly, with the migrations in this assembly. A larger
/// application can derive from it: override <see cref="ConfigureHostModel"/> to add its own entities
/// (the conventions are applied after it, so they cover those too), <see cref="CurrentUserId"/> to
/// stamp the signed-in user, and <see cref="OnSaving"/> to stamp entities that are not a
/// <see cref="BaseEntity"/>. A derived context keeps its own migrations; it never runs these.
/// </para>
/// </summary>
public class EngineDbContext : DbContext
{
    public EngineDbContext(DbContextOptions<EngineDbContext> options) : base(options) { }

    /// <summary>For derived contexts, which pass their own typed options.</summary>
    protected EngineDbContext(DbContextOptions options) : base(options) { }

    // The table names come from these property names (snake-cased: workflow_instances, …), so a
    // derived context must not redeclare them under other names.
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<WorkflowNodeState> WorkflowNodeStates => Set<WorkflowNodeState>();
    public DbSet<WorkflowVariable> WorkflowVariables => Set<WorkflowVariable>();
    public DbSet<GateDecision> GateDecisions => Set<GateDecision>();

    /// <summary>The user stamped into CreatedBy/UpdatedBy. None by default.</summary>
    protected virtual Guid? CurrentUserId => null;

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EngineDbContext).Assembly);
        ConfigureHostModel(modelBuilder);

        // Last, so they cover every entity, the host's included.
        ApplySoftDeleteQueryFilter(modelBuilder);
        ApplySnakeCaseNaming(modelBuilder);
    }

    /// <summary>Adds a derived context's own entities, configuration and seed data.</summary>
    protected virtual void ConfigureHostModel(ModelBuilder modelBuilder) { }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    /// <summary>Called on every save after the <see cref="BaseEntity"/> stamps, with the same timestamp.</summary>
    protected virtual void OnSaving(DateTime now) { }

    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;
        var userId = CurrentUserId;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    if (entry.Entity is AuditableEntity addedAudit)
                    {
                        addedAudit.CreatedBy ??= userId;
                        addedAudit.UpdatedBy = userId;
                    }
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    if (entry.Entity is AuditableEntity modifiedAudit)
                    {
                        modifiedAudit.UpdatedBy = userId;
                    }
                    break;
            }
        }

        OnSaving(now);
    }

    private static void ApplySoftDeleteQueryFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var prop = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
            var notDeleted = Expression.Equal(prop, Expression.Constant(false));
            var lambda = Expression.Lambda(notDeleted, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    private static void ApplySnakeCaseNaming(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is not null)
            {
                entity.SetTableName(NamingConventions.ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                var storeId = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                var columnName = property.GetColumnName(storeId);
                if (columnName is not null)
                {
                    property.SetColumnName(NamingConventions.ToSnakeCase(columnName));
                }
            }

            foreach (var key in entity.GetKeys())
            {
                var name = key.GetName();
                if (name is not null) key.SetName(NamingConventions.ToSnakeCase(name));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                var name = fk.GetConstraintName();
                if (name is not null) fk.SetConstraintName(NamingConventions.ToSnakeCase(name));
            }

            foreach (var index in entity.GetIndexes())
            {
                var name = index.GetDatabaseName();
                if (name is not null) index.SetDatabaseName(NamingConventions.ToSnakeCase(name));
            }
        }
    }
}
