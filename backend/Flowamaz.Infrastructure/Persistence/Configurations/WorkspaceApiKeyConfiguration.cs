using Flowamaz.Core.Entities.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceApiKeyConfiguration : IEntityTypeConfiguration<WorkspaceApiKey>
{
    public void Configure(EntityTypeBuilder<WorkspaceApiKey> builder)
    {
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Name).HasMaxLength(128).IsRequired();

        // 64 hex chars = SHA256. Unique so a hash maps to at most one key.
        builder.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(k => k.KeyHash).IsUnique();

        builder.Property(k => k.KeyPrefix).HasMaxLength(32).IsRequired();

        builder.Property(k => k.Scopes)
            .HasConversion(JsonColumn.Converter<List<string>>(), JsonColumn.Comparer<List<string>>())
            .HasColumnType("jsonb");

        builder.HasIndex(k => k.WorkspaceId);
    }
}
