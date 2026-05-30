namespace Flowamaz.Core.Exceptions;

/// <summary>
/// Billing/Stripe failures (prompt 07-01). Carries an actionable message + HTTP status. Used for
/// invalid webhook signatures (400), unknown plans (404), and unconfigured-billing states (503).
/// </summary>
public sealed class BillingException : AppException
{
    public BillingException(string errorCode, string message, int httpStatusCode = 400)
        : base(errorCode, message, httpStatusCode)
    {
    }

    public static BillingException InvalidWebhookSignature() => new(
        "STRIPE_WEBHOOK_SIGNATURE_INVALID",
        "The Stripe webhook signature could not be verified. The request was rejected. " +
        "Confirm STRIPE_WEBHOOK_SECRET matches the endpoint's signing secret in the Stripe dashboard.",
        httpStatusCode: 400);

    public static BillingException PlanNotFound(Guid planId) => new(
        "BILLING_PLAN_NOT_FOUND",
        $"No plan exists with id '{planId}'. Pick a plan from GET /api/v1/billing/plans and retry checkout.",
        httpStatusCode: 404);

    public static BillingException PlanNotPurchasable(string planName) => new(
        "BILLING_PLAN_NOT_PURCHASABLE",
        $"The {planName} plan can't be purchased through self-service checkout. " +
        "Community is free, and Enterprise is contract-only — contact sales@flowamaz.io.",
        httpStatusCode: 400);

    public static BillingException NotConfigured() => new(
        "BILLING_NOT_CONFIGURED",
        "Billing is not configured on this deployment. Set STRIPE_SECRET_KEY (and the plan price ids) " +
        "to enable checkout, or contact your administrator.",
        httpStatusCode: 503);

    public static BillingException NoCustomer() => new(
        "BILLING_NO_CUSTOMER",
        "This organisation has no billing account yet. Start a subscription via Upgrade before opening " +
        "the billing portal.",
        httpStatusCode: 400);
}
