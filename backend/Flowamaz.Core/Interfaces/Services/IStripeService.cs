namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Stripe billing integration (prompt 07-01): Checkout for plan upgrades, the self-service Customer
/// Portal, and webhook-driven subscription lifecycle. All Stripe secrets/customer ids stay server-side
/// and are never returned to the frontend. Webhook signatures are verified before any state change.
/// </summary>
public interface IStripeService
{
    /// <summary>
    /// Create a Stripe Checkout Session for <paramref name="planId"/> on the given billing cycle and
    /// return the hosted checkout URL to redirect the browser to. Pre-fills the org owner's email and
    /// stamps metadata { orgId, planId } so the webhook can reconcile the result.
    /// </summary>
    Task<string> CreateCheckoutSessionAsync(
        Guid orgId,
        Guid planId,
        bool annual,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a Stripe Customer Portal session (self-service upgrade/downgrade/cancel/payment method)
    /// and return the portal URL. Requires the org to already have a Stripe customer id.
    /// </summary>
    Task<string> CreateCustomerPortalSessionAsync(
        Guid orgId,
        string returnUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify the Stripe webhook signature and dispatch the event to the relevant handler. Idempotent:
    /// already-processed event ids are skipped. Throws on an invalid signature (caller maps to 400).
    /// </summary>
    Task HandleWebhookAsync(string payload, string stripeSignature, CancellationToken cancellationToken = default);
}
