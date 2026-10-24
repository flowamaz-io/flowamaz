namespace Flowamaz.Core.Models;

// ── Workflow Economy (FUNCTIONAL.md §8.9) — API + gateway models. Amounts are minor units. ──

public sealed record PublisherStatus(
    bool Connected,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool DetailsSubmitted,
    IReadOnlyList<string> RequirementsDue,
    string? Country,
    bool CanSellPaidWorkflows,
    decimal PlatformFeePercent);

public sealed record PublisherListing(
    Guid TemplateId,
    string Name,
    string PricingModel,
    int PriceCents,
    string Currency,
    string ReviewStatus,
    int InstallCount,
    int SalesCount,
    int NetCents);

public sealed record PublisherEarningEntry(
    Guid Id,
    Guid TemplateId,
    string TemplateName,
    string Type,
    int GrossCents,
    int FeeCents,
    int NetCents,
    string Currency,
    DateTime OccurredAt);

public sealed record PublisherEarningsSummary(
    string Currency,
    long GrossCents,
    long FeeCents,
    long NetCents,
    int SalesCount,
    int ActiveSubscriptions,
    IReadOnlyList<PublisherListing> Listings,
    IReadOnlyList<PublisherEarningEntry> Entries);

public sealed record TemplatePricingRequest(string PricingModel, int PriceCents, string Currency);

public sealed record TemplateAccess(
    bool RequiresPurchase,
    bool Owned,
    string Status,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

public sealed record TemplatePurchaseItem(
    Guid Id,
    Guid TemplateId,
    string TemplateName,
    string PricingModel,
    int AmountCents,
    string Currency,
    string Status,
    DateTime? PurchasedAt,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

/// <summary>Connected-account state as reported by Stripe.</summary>
public sealed record ConnectAccountStatus(
    string AccountId,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool DetailsSubmitted,
    string? Country,
    IReadOnlyList<string> RequirementsDue);

/// <summary>Everything the gateway needs to open a destination-charge Checkout session.</summary>
public sealed record TemplateCheckoutRequest(
    Guid PurchaseId,
    Guid TemplateId,
    Guid WorkspaceId,
    string TemplateName,
    bool Monthly,
    int AmountCents,
    string Currency,
    int FeeCents,
    decimal FeePercent,
    string DestinationAccountId,
    string? CustomerEmail,
    string SuccessUrl,
    string CancelUrl);

public sealed record TemplateCheckoutSession(string SessionId, string Url);

/// <summary>Normalised Stripe webhook payloads handed to the economy service.</summary>
public sealed record EconomyCheckoutCompleted(
    string SessionId,
    IReadOnlyDictionary<string, string> Metadata,
    string? PaymentStatus,
    string? PaymentIntentId,
    string? SubscriptionId,
    long? AmountTotal,
    string? Currency);

public sealed record EconomyInvoicePaid(
    string InvoiceId,
    string? SubscriptionId,
    string? BillingReason,
    long AmountPaid,
    string? Currency,
    DateTime? PeriodEnd);

public sealed record EconomySubscriptionChanged(
    string SubscriptionId,
    string Status,
    bool CancelAtPeriodEnd,
    DateTime? CurrentPeriodEnd,
    bool Deleted);

public sealed record EconomyRefund(string RefundId, long Amount);

public sealed record EconomyChargeRefunded(
    string ChargeId,
    string? PaymentIntentId,
    long Amount,
    long AmountRefunded,
    IReadOnlyList<EconomyRefund> Refunds);
