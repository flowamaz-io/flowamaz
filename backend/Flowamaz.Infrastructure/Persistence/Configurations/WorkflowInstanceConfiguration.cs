using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.EnvironmentType).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.TriggerType).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.SagaState).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.TriggerPayload).HasColumnType("jsonb");
        builder.Property(i => i.CorrelationId).HasMaxLength(256);
        builder.Property(i => i.CurrentNodeId).HasMaxLength(128);
        builder.Property(i => i.WorkerLeaseId).HasMaxLength(64);
        builder.Property(i => i.IdempotencyKey).HasMaxLength(128);
        builder.Property(i => i.SagaStrategy).HasMaxLength(64);

        builder.HasIndex(i => i.WorkspaceId);
        builder.HasIndex(i => new { i.Status, i.WorkerLeaseExpiresAt });
        builder.HasIndex(i => i.IdempotencyKey).IsUnique();
        builder.HasIndex(i => new { i.IsTest, i.TestExpiresAt });
        builder.HasIndex(i => i.ParentInstanceId);
    }
}
