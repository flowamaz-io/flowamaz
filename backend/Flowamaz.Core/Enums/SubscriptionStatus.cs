namespace Flowamaz.Core.Enums;

/// <summary>Billing state of a <see cref="Entities.Platform.Subscription"/>.</summary>
public enum SubscriptionStatus
{
    Active,
    PastDue,
    Cancelled,
}
