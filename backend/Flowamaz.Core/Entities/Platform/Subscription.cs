using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Platform;

/// <summary>
/// The billing subscription linking an organisation to a plan for a billing period.
/// Not soft-deleted: cancelled subscriptions are retained for billing history.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid PlanId { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }

    /// <summary>Hard monthly overage cap in USD (FUNCTIONAL.md §13.4). 0 = no extra cap.</summary>
    public decimal OvercapCapUsd { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    /// <summary>The Stripe subscription id (sub_...) once a paid plan is activated (prompt 07-01).</summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Trial expiry when the org is in a free trial; null once on a paid subscription.</summary>
    public DateTime? TrialEndsAt { get; set; }

    /// <summary>Set when the subscription is cancelled via the Stripe portal.</summary>
    public DateTime? CanceledAt { get; set; }

    /// <summary>True after invoice.payment_failed; cleared on invoice.payment_succeeded (prompt 07-01).</summary>
    public bool PaymentFailed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
