using Flowamaz.Core.Entities.Economy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class PublisherAccountConfiguration : IEntityTypeConfiguration<PublisherAccount>
{
    public void Configure(EntityTypeBuilder<PublisherAccount> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.StripeAccountId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Country).HasMaxLength(2);

        var converter = new ValueConverter<string[], string>(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>());
        var comparer = new ValueComparer<string[]>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (a, x) => HashCode.Combine(a, x.GetHashCode())),
            v => v.ToArray());
        builder.Property(p => p.RequirementsDue).HasConversion(converter, comparer).HasColumnType("jsonb");

        builder.HasIndex(p => p.OrgId).IsUnique();
        builder.HasIndex(p => p.StripeAccountId).IsUnique();
    }
}

public sealed class TemplatePurchaseConfiguration : IEntityTypeConfiguration<TemplatePurchase>
{
    public void Configure(EntityTypeBuilder<TemplatePurchase> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PricingModel).HasMaxLength(16).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasMaxLength(16).IsRequired();
        builder.Property(p => p.StripeCheckoutSessionId).HasMaxLength(255);
        builder.Property(p => p.StripePaymentIntentId).HasMaxLength(255);
        builder.Property(p => p.StripeSubscriptionId).HasMaxLength(255);

        builder.HasIndex(p => new { p.WorkspaceId, p.TemplateId });
        builder.HasIndex(p => p.StripeCheckoutSessionId).IsUnique().HasFilter("stripe_checkout_session_id IS NOT NULL");
        builder.HasIndex(p => p.StripeSubscriptionId).HasFilter("stripe_subscription_id IS NOT NULL");
        builder.HasIndex(p => p.StripePaymentIntentId).HasFilter("stripe_payment_intent_id IS NOT NULL");
        builder.HasIndex(p => p.PublisherOrgId);
    }
}

public sealed class PublisherEarningConfiguration : IEntityTypeConfiguration<PublisherEarning>
{
    public void Configure(EntityTypeBuilder<PublisherEarning> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).HasMaxLength(16).IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.StripeObjectId).HasMaxLength(255).IsRequired();
        // Idempotency: one ledger row per Stripe object (session / invoice / refund).
        builder.HasIndex(e => new { e.StripeObjectId, e.Type }).IsUnique();
        builder.HasIndex(e => new { e.PublisherOrgId, e.OccurredAt });
    }
}
