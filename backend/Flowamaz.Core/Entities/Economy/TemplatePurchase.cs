namespace Flowamaz.Core.Entities.Economy;

/// <summary>
/// A workspace's entitlement to a paid workflow template — created "pending" when Checkout starts
/// and activated by the Stripe webhook. One-time purchases stay active; monthly subscriptions follow
/// the Stripe subscription (renewals extend <see cref="CurrentPeriodEnd"/>, deletion cancels).
/// </summary>
public class TemplatePurchase : WorkspaceEntity
{
    public Guid OrgId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid PublisherOrgId { get; set; }
    /// <summary>"one_time" or "monthly" — copied from the listing at purchase time.</summary>
    public string PricingModel { get; set; } = TemplatePricing.OneTime;
    public int AmountCents { get; set; }
    public int FeeCents { get; set; }
    public string Currency { get; set; } = "usd";
    /// <summary>pending | active | past_due | canceled | refunded.</summary>
    public string Status { get; set; } = TemplatePurchaseStatus.Pending;
    public string? StripeCheckoutSessionId { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTime? PurchasedAt { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public Guid? PurchasedByUserId { get; set; }
}

public static class TemplatePurchaseStatus
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string PastDue = "past_due";
    public const string Canceled = "canceled";
    public const string Refunded = "refunded";
}

public static class TemplatePricing
{
    public const string Free = "free";
    public const string OneTime = "one_time";
    public const string Monthly = "monthly";

    public static readonly IReadOnlySet<string> Currencies = new HashSet<string> { "usd", "eur", "gbp", "myr", "sgd", "inr" };
    public const int MinPriceCents = 100;
    public const int MaxPriceCents = 1_000_000;

    public static bool IsPaid(string? model) => model is OneTime or Monthly;

    public static string Normalize(string? model) => model?.Trim().ToLowerInvariant().Replace('-', '_') switch
    {
        "one_time" or "onetime" => OneTime,
        "monthly" or "subscription" or "subscription_monthly" => Monthly,
        _ => Free,
    };

    /// <summary>Platform fee in minor units, rounded half-up (e.g. 20% of 4999 = 1000).</summary>
    public static int Fee(int amountCents, decimal feePercent) =>
        (int)Math.Round(amountCents * feePercent / 100m, MidpointRounding.AwayFromZero);
}
