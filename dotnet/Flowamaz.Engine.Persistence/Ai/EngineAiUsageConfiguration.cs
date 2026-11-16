using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Ai;

public sealed class EngineAiUsageConfiguration : IEntityTypeConfiguration<EngineAiUsage>
{
    public void Configure(EntityTypeBuilder<EngineAiUsage> builder)
    {
        builder.ToTable("EngineAiUsage"); // snake-cased to engine_ai_usage
        builder.HasKey(u => u.Id);
        builder.Property(u => u.NodeId).HasMaxLength(128);
        builder.Property(u => u.Provider).HasMaxLength(64).IsRequired();
        builder.Property(u => u.ModelId).HasMaxLength(128).IsRequired();
        builder.Property(u => u.FunctionId).HasMaxLength(64).IsRequired();
        builder.Property(u => u.CostUsd).HasPrecision(18, 8);

        // Usage per workspace over time (budgets, reports) and per run.
        builder.HasIndex(u => new { u.WorkspaceId, u.CreatedAt });
        builder.HasIndex(u => u.InstanceId);
    }
}
