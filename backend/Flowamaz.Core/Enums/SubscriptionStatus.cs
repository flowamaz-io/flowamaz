namespace Flowamaz.Core.Enums;

/// <summary>Billing state of a <see cref="Entities.Platform.Subscription"/>.</summary>
public enum SubscriptionStatus
{
    Active,
    PastDue,
    Cancelled,

    /// <summary>In a free trial — no active Stripe subscription yet (prompt 07-01).</summary>
    Trialing,

    /// <summary>Stripe subscription paused (e.g. payment method removed) (prompt 07-01).</summary>
    Paused,
}
