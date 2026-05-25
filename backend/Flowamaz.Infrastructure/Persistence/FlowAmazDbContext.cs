using System.Linq.Expressions;
using Flowamaz.Core.Entities;
using Flowamaz.Core.Entities.Ai;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Flowamaz.Infrastructure.Persistence;

/// <summary>
/// Single application DbContext. Applies global conventions on construction:
/// snake_case naming on tables/columns/keys/indexes, soft-delete query filter on every
/// <see cref="BaseEntity"/>, and CreatedBy/UpdatedBy stamping from <see cref="ICurrentUserService"/>
/// on every <see cref="AuditableEntity"/>.
/// </summary>
public class FlowAmazDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUser;

    public FlowAmazDbContext(DbContextOptions<FlowAmazDbContext> options, ICurrentUserService? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<AiTokenUsage> AiTokenUsage => Set<AiTokenUsage>();
    public DbSet<WorkspaceAiBudget> WorkspaceAiBudgets => Set<WorkspaceAiBudget>();
    public DbSet<PlatformAiConfig> PlatformAiConfigs => Set<PlatformAiConfig>();
    public DbSet<OrgAiConfig> OrgAiConfigs => Set<OrgAiConfig>();
    public DbSet<WorkspaceAiConfig> WorkspaceAiConfigs => Set<WorkspaceAiConfig>();
    public DbSet<ModelCatalogue> ModelCatalogue => Set<ModelCatalogue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureAiTokenUsage(modelBuilder);
        ConfigureWorkspaceAiBudget(modelBuilder);
        ConfigurePlatformAiConfig(modelBuilder);
        ConfigureOrgAiConfig(modelBuilder);
        ConfigureWorkspaceAiConfig(modelBuilder);
        ConfigureModelCatalogue(modelBuilder);

        ApplySoftDeleteQueryFilter(modelBuilder);
        ApplySnakeCaseNaming(modelBuilder);

        SeedAi(modelBuilder);
    }

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

    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;
        var userId = _currentUser?.UserId;

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

        // Config tables don't inherit BaseEntity but still carry CreatedAt/UpdatedAt — stamp them too.
        StampConfigEntity<WorkspaceAiBudget>(now, b => b.UpdatedAt = now);
        StampConfigEntity<PlatformAiConfig>(now, b => b.UpdatedAt = now);
        StampConfigEntity<OrgAiConfig>(now, b => b.UpdatedAt = now);
        StampConfigEntity<WorkspaceAiConfig>(now, b => b.UpdatedAt = now);
        StampConfigEntity<ModelCatalogue>(now, b => b.UpdatedAt = now);
    }

    private void StampConfigEntity<T>(DateTime now, Action<T> setUpdated) where T : class
    {
        foreach (var entry in ChangeTracker.Entries<T>())
        {
            if (entry.State == EntityState.Modified)
            {
                setUpdated(entry.Entity);
            }
        }
    }

    private static void ConfigureAiTokenUsage(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AiTokenUsage>();
        e.HasKey(x => x.Id);
        e.Property(x => x.FunctionId).HasMaxLength(64).IsRequired();
        e.Property(x => x.ModelId).HasMaxLength(128).IsRequired();
        e.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        e.Property(x => x.CostUsd).HasPrecision(18, 8);
        e.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
        e.HasIndex(x => new { x.OrgId, x.CreatedAt });
        e.HasIndex(x => new { x.FunctionId, x.CreatedAt });
    }

    private static void ConfigureWorkspaceAiBudget(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<WorkspaceAiBudget>();
        e.HasKey(x => x.WorkspaceId);
        e.HasIndex(x => x.BudgetResetDate);
    }

    private static void ConfigurePlatformAiConfig(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PlatformAiConfig>();
        e.HasKey(x => x.FunctionId);
        e.Property(x => x.FunctionId).HasMaxLength(64);
        e.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        e.Property(x => x.ModelId).HasMaxLength(128).IsRequired();
        e.Property(x => x.KeySource).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.PlanAccess).HasColumnType("jsonb");
    }

    private static void ConfigureOrgAiConfig(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<OrgAiConfig>();
        e.HasKey(x => x.OrgId);
        e.Property(x => x.ProvidersEnabled).HasColumnType("jsonb");
        e.Property(x => x.WorkspaceLockedFunctions).HasColumnType("jsonb");
        e.Property(x => x.FunctionOverridesJson).HasColumnType("jsonb");
    }

    private static void ConfigureWorkspaceAiConfig(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<WorkspaceAiConfig>();
        e.HasKey(x => x.WorkspaceId);
        e.Property(x => x.FunctionOverridesJson).HasColumnType("jsonb");
        e.Property(x => x.ByokCredentialRefsJson).HasColumnType("jsonb");
    }

    private static void ConfigureModelCatalogue(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ModelCatalogue>();
        e.HasKey(x => x.ModelId);
        e.Property(x => x.ModelId).HasMaxLength(128);
        e.Property(x => x.ProviderModelId).HasMaxLength(128).IsRequired();
        e.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        e.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        e.Property(x => x.PlanAccess).HasColumnType("jsonb");
        e.HasIndex(x => x.Provider);
    }

    private static void SeedAi(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelCatalogue>().HasData(AiSeedData.Models);
        modelBuilder.Entity<PlatformAiConfig>().HasData(AiSeedData.PlatformConfig);
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
