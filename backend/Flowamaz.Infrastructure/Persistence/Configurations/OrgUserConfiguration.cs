using Flowamaz.Core.Entities.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class OrgUserConfiguration : IEntityTypeConfiguration<OrgUser>
{
    public void Configure(EntityTypeBuilder<OrgUser> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.Name).HasMaxLength(256).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();

        // Email is unique per org, not globally (FUNCTIONAL.md §4 / prompt 02).
        builder.HasIndex(u => new { u.OrgId, u.Email }).IsUnique();
    }
}
