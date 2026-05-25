using Flowamaz.Core.Entities.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceEnvironmentConfiguration : IEntityTypeConfiguration<WorkspaceEnvironment>
{
    public void Configure(EntityTypeBuilder<WorkspaceEnvironment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasConversion<string>().HasMaxLength(32).IsRequired();

        // Exactly one of each environment per workspace.
        builder.HasIndex(e => new { e.WorkspaceId, e.Name }).IsUnique();
    }
}
