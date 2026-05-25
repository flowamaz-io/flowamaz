using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowNodeStateConfiguration : IEntityTypeConfiguration<WorkflowNodeState>
{
    public void Configure(EntityTypeBuilder<WorkflowNodeState> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.NodeId).HasMaxLength(128).IsRequired();
        builder.Property(n => n.NodeType).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.InputPayload).HasColumnType("jsonb");
        builder.Property(n => n.OutputPayload).HasColumnType("jsonb");

        builder.HasIndex(n => new { n.InstanceId, n.NodeId }).IsUnique();
        builder.HasIndex(n => n.WorkspaceId);
    }
}
