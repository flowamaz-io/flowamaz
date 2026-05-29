using Flowamaz.Core.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowRoiConfigConfiguration : IEntityTypeConfiguration<WorkflowRoiConfig>
{
    public void Configure(EntityTypeBuilder<WorkflowRoiConfig> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ManualProcessCostPerRunUsd).HasPrecision(18, 2);
        builder.Property(c => c.AutomationCostPerRunUsd).HasPrecision(18, 2);
        builder.Property(c => c.MonthlyCurrency).HasMaxLength(8);
        builder.HasIndex(c => new { c.WorkflowDefinitionId, c.WorkspaceId }).IsUnique();
        builder.HasIndex(c => c.WorkspaceId);
    }
}
