using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowVariableConfiguration : IEntityTypeConfiguration<WorkflowVariable>
{
    public void Configure(EntityTypeBuilder<WorkflowVariable> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).HasMaxLength(256).IsRequired();
        builder.Property(v => v.Value).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(v => new { v.InstanceId, v.Name }).IsUnique();
        builder.HasIndex(v => v.WorkspaceId);
    }
}
