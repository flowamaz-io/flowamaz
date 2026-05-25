using Flowamaz.Core.Entities.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class UsageAggregateConfiguration : IEntityTypeConfiguration<UsageAggregate>
{
    public void Configure(EntityTypeBuilder<UsageAggregate> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.PeriodMonth).HasMaxLength(7).IsRequired();
        builder.Property(u => u.AiCostUsd).HasPrecision(18, 8);
        builder.HasIndex(u => new { u.OrgId, u.PeriodMonth }).IsUnique();
    }
}
