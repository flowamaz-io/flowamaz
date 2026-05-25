using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).HasMaxLength(256).IsRequired();
        builder.Property(w => w.Slug).HasMaxLength(128).IsRequired();
        builder.Property(w => w.YamlContent).IsRequired();
        builder.Property(w => w.CurrentVersion).HasMaxLength(64).IsRequired();
        builder.Property(w => w.CreatedByMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(w => w.TriggerType).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(w => w.WorkspaceId);
        builder.HasIndex(w => new { w.WorkspaceId, w.Slug }).IsUnique();
    }
}
