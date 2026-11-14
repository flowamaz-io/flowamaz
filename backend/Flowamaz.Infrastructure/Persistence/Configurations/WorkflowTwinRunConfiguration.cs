using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowTwinRunConfiguration : IEntityTypeConfiguration<WorkflowTwinRun>
{
    public void Configure(EntityTypeBuilder<WorkflowTwinRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.VersionLabel).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Trigger).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.FindingsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);
        builder.HasIndex(r => new { r.WorkspaceId, r.WorkflowDefinitionId, r.CompletedAt });
    }
}
