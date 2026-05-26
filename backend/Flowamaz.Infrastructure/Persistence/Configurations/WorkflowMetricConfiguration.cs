using Flowamaz.Core.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowMetricConfiguration : IEntityTypeConfiguration<WorkflowMetric>
{
    public void Configure(EntityTypeBuilder<WorkflowMetric> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.BottleneckNodeId).HasMaxLength(128);
        builder.HasIndex(m => new { m.WorkflowDefinitionId, m.PeriodHour }).IsUnique();
        builder.HasIndex(m => m.WorkspaceId);
    }
}
