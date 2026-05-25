using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(128).IsRequired();
        builder.HasIndex(p => p.Slug).IsUnique();
        builder.Property(p => p.PriceMonthlyUsd).HasPrecision(18, 2);
        builder.Property(p => p.PriceAnnualUsd).HasPrecision(18, 2);

        builder.Property(p => p.Limits)
            .HasConversion(JsonColumn.Converter<PlanLimits>(), JsonColumn.Comparer<PlanLimits>())
            .HasColumnType("jsonb");
        builder.Property(p => p.Features)
            .HasConversion(JsonColumn.Converter<PlanFeatures>(), JsonColumn.Comparer<PlanFeatures>())
            .HasColumnType("jsonb");

        builder.HasData(PlatformSeedData.Plans);
    }
}
