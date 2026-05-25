using Flowamaz.Core.Entities.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class OrganisationConfiguration : IEntityTypeConfiguration<Organisation>
{
    public void Configure(EntityTypeBuilder<Organisation> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Name).HasMaxLength(256).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(128).IsRequired();
        builder.HasIndex(o => o.Slug).IsUnique();
        builder.Property(o => o.BillingEmail).HasMaxLength(256).IsRequired();
        builder.Property(o => o.StripeCustomerId).HasMaxLength(128);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.DataRegion).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(o => o.PlanId);
    }
}
