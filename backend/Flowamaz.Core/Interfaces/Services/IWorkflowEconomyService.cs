using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Workflow Economy (FUNCTIONAL.md §8.9): publisher onboarding (Stripe Connect Express), listing
/// prices, paid installs through Checkout with the platform fee, entitlements, and the publisher
/// earnings ledger. Webhook handlers return true when the event belonged to the economy.
/// </summary>
public interface IWorkflowEconomyService
{
    decimal PlatformFeePercent { get; }

    Task<PublisherStatus> GetPublisherStatusAsync(Guid orgId, CancellationToken ct = default);
    Task<string> StartOnboardingAsync(Guid orgId, string returnUrl, string refreshUrl, CancellationToken ct = default);
    Task<string> CreateDashboardLinkAsync(Guid orgId, CancellationToken ct = default);
    Task<PublisherEarningsSummary> GetEarningsAsync(Guid orgId, CancellationToken ct = default);
    Task<PublisherListing> SetPricingAsync(Guid orgId, Guid templateId, TemplatePricingRequest pricing, CancellationToken ct = default);

    /// <summary>Validates a listing price and (for paid) that the org can receive payouts. Throws EconomyException.</summary>
    Task ValidatePricingAsync(Guid orgId, TemplatePricingRequest pricing, CancellationToken ct = default);

    Task<TemplateAccess> GetAccessAsync(Guid workspaceId, Guid orgId, WorkflowTemplate template, CancellationToken ct = default);
    Task<bool> HasEntitlementAsync(Guid workspaceId, Guid orgId, WorkflowTemplate template, CancellationToken ct = default);
    Task<string> StartPurchaseAsync(Guid workspaceId, Guid orgId, Guid userId, Guid templateId, string successUrl, string cancelUrl, CancellationToken ct = default);
    Task<IReadOnlyList<TemplatePurchaseItem>> ListPurchasesAsync(Guid workspaceId, CancellationToken ct = default);
    Task<TemplatePurchaseItem> CancelPurchaseAsync(Guid workspaceId, Guid purchaseId, CancellationToken ct = default);

    Task<bool> HandleCheckoutCompletedAsync(EconomyCheckoutCompleted e, CancellationToken ct = default);
    Task<bool> HandleInvoicePaidAsync(EconomyInvoicePaid e, CancellationToken ct = default);
    Task<bool> HandleInvoicePaymentFailedAsync(string? subscriptionId, CancellationToken ct = default);
    Task<bool> HandleSubscriptionChangedAsync(EconomySubscriptionChanged e, CancellationToken ct = default);
    Task<bool> HandleChargeRefundedAsync(EconomyChargeRefunded e, CancellationToken ct = default);
    Task HandleAccountUpdatedAsync(ConnectAccountStatus status, CancellationToken ct = default);
}
