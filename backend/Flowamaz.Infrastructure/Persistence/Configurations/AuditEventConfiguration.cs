using Flowamaz.Core.Entities.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ActorType).HasMaxLength(16).IsRequired();
        builder.Property(e => e.ActorLabel).HasMaxLength(256);
        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ResourceType).HasMaxLength(32).IsRequired();
        builder.Property(e => e.ResourceLabel).HasMaxLength(512);
        builder.Property(e => e.Action).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Metadata).HasColumnType("jsonb");
        builder.Property(e => e.IpAddress).HasMaxLength(64);
        builder.Property(e => e.UserAgent).HasMaxLength(512);

        // Hot path: workspace audit feed and org-wide audit feed, both newest-first.
        builder.HasIndex(e => new { e.WorkspaceId, e.CreatedAt });
        builder.HasIndex(e => new { e.OrgId, e.CreatedAt });
    }
}
