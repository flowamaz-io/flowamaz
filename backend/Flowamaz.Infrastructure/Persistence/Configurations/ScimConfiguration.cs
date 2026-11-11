using Flowamaz.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class ScimTokenConfiguration : IEntityTypeConfiguration<ScimToken>
{
    public void Configure(EntityTypeBuilder<ScimToken> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.TokenPrefix).HasMaxLength(16).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.OrgId);
    }
}

public sealed class ScimGroupConfiguration : IEntityTypeConfiguration<ScimGroup>
{
    public void Configure(EntityTypeBuilder<ScimGroup> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(g => g.ExternalId).HasMaxLength(256);
        builder.HasIndex(g => new { g.OrgId, g.DisplayName }).IsUnique();
    }
}

public sealed class ScimGroupMemberConfiguration : IEntityTypeConfiguration<ScimGroupMember>
{
    public void Configure(EntityTypeBuilder<ScimGroupMember> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.GroupId, m.OrgUserId }).IsUnique();
        builder.HasIndex(m => m.OrgUserId);
        builder.HasOne<ScimGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Flowamaz.Core.Entities.Platform.OrgUser>().WithMany().HasForeignKey(m => m.OrgUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
