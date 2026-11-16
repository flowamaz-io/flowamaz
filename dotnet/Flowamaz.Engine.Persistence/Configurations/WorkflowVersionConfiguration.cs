using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowVersionConfiguration : IEntityTypeConfiguration<WorkflowVersion>
{
    public void Configure(EntityTypeBuilder<WorkflowVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.CommitSha).HasMaxLength(64).IsRequired();
        builder.Property(v => v.TagName).HasMaxLength(128);
        builder.Property(v => v.BranchName).HasMaxLength(128).IsRequired();
        builder.Property(v => v.YamlContent).IsRequired();
        builder.Property(v => v.Message).HasMaxLength(512).IsRequired();

        builder.HasIndex(v => v.WorkspaceId);
        builder.HasIndex(v => new { v.WorkflowDefinitionId, v.CommitSha }).IsUnique();
    }
}
