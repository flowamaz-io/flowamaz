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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
