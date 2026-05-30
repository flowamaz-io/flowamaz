using Flowamaz.Core.Entities.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.BillingCycle).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(s => s.OvercapCapUsd).HasPrecision(18, 2);
        builder.Property(s => s.StripeSubscriptionId).HasMaxLength(128);
        builder.HasIndex(s => s.OrgId);
        builder.HasIndex(s => s.StripeSubscriptionId);
    }
}
