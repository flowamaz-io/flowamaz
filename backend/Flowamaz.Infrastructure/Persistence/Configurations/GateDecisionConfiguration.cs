using Flowamaz.Core.Entities.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class GateDecisionConfiguration : IEntityTypeConfiguration<GateDecision>
{
    public void Configure(EntityTypeBuilder<GateDecision> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.NodeId).HasMaxLength(128).IsRequired();
        builder.Property(g => g.AssignedToEmail).HasMaxLength(256);
        builder.Property(g => g.Decision).HasConversion<string>().HasMaxLength(32);
        builder.Property(g => g.DeliveryChannel).HasConversion<string>().HasMaxLength(32);
        builder.Property(g => g.DeliveryStatus).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(g => new { g.InstanceId, g.NodeId }).IsUnique();
        builder.HasIndex(g => g.AssignedTo);
        builder.HasIndex(g => g.ExpiresAt);
        builder.HasIndex(g => g.WorkspaceId);
    }
}
