using Flowamaz.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class CliSessionConfiguration : IEntityTypeConfiguration<CliSession>
{
    public void Configure(EntityTypeBuilder<CliSession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(s => s.TokenHash).IsUnique();
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.CreatedByIp).HasMaxLength(64);
        builder.Property(s => s.LastUsedIp).HasMaxLength(64);
        builder.Property(s => s.CurrentAccessTokenId).HasMaxLength(64);
        builder.HasIndex(s => s.OrgUserId);
        builder.HasIndex(s => s.OrgId);
        builder.Ignore(s => s.IsActive); // computed, not persisted
    }
}
