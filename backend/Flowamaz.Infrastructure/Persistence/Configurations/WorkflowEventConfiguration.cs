using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

/// <summary>
/// Immutable event log. Deliberately not a <c>BaseEntity</c> — no soft-delete column or query
/// filter, so every row stays visible forever.
/// </summary>
public sealed class WorkflowEventConfiguration : IEntityTypeConfiguration<WorkflowEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.NodeId).HasMaxLength(128);
        builder.Property(e => e.NodeType).HasMaxLength(64);
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.InputSnapshot).HasColumnType("jsonb");
        builder.Property(e => e.OutputSnapshot).HasColumnType("jsonb");
        builder.Property(e => e.ErrorSnapshot).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.InstanceId, e.SequenceNumber }).IsUnique();
        builder.HasIndex(e => new { e.InstanceId, e.OccurredAt });
        builder.HasIndex(e => e.WorkspaceId);
    }
}
