using Flowamaz.Core.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowInsightConfiguration : IEntityTypeConfiguration<WorkflowInsight>
{
    public void Configure(EntityTypeBuilder<WorkflowInsight> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.InsightType).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.Message).HasMaxLength(1024).IsRequired();
        builder.Property(i => i.Data).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(i => new { i.WorkspaceId, i.IsAcknowledged, i.CreatedAt });
        builder.HasIndex(i => i.WorkflowDefinitionId);
    }
}
