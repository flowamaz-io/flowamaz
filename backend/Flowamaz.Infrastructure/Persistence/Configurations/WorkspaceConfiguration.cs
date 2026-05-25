using Flowamaz.Core.Entities.Workspaces;
using Flowamaz.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).HasMaxLength(256).IsRequired();
        builder.Property(w => w.Slug).HasMaxLength(128).IsRequired();

        // Slug is unique per org, not globally (FUNCTIONAL.md §2.1).
        builder.HasIndex(w => new { w.OrgId, w.Slug }).IsUnique();

        builder.Property(w => w.Settings)
            .HasConversion(JsonColumn.Converter<WorkspaceSettings>(), JsonColumn.Comparer<WorkspaceSettings>())
            .HasColumnType("jsonb");
    }
}
