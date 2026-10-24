namespace Flowamaz.Core.Exceptions;

/// <summary>Workflow Economy failures (paid templates, publisher payouts). Every message says what to do next.</summary>
public sealed class EconomyException : AppException
{
    public EconomyException(string errorCode, string message, int httpStatusCode = 400)
        : base(errorCode, message, httpStatusCode)
    {
    }

    public static EconomyException NotConfigured() => new(
        "ECONOMY_NOT_CONFIGURED",
        "Paid workflows are not available on this deployment because Stripe is not configured. " +
        "Set STRIPE_SECRET_KEY (a test key, sk_test_…, is fine) and enable Connect in the Stripe dashboard.",
        503);

    public static EconomyException PurchaseRequired(string templateName) => new(
        "TEMPLATE_PURCHASE_REQUIRED",
        $"'{templateName}' is a paid workflow. Buy it from its template page (organisation owners only), then install it.",
        402);

    public static EconomyException PublisherNotReady() => new(
        "PUBLISHER_NOT_READY",
        "To sell workflows your organisation needs a verified payout account. Open Settings → Publisher, " +
        "click 'Connect with Stripe' and finish onboarding, then set a price.",
        422);

    public static EconomyException InvalidPrice(string reason) => new("INVALID_TEMPLATE_PRICE", reason, 422);

    public static EconomyException TemplateNotFound(Guid id) => new(
        "TEMPLATE_NOT_FOUND",
        $"Template '{id}' was not found or your organisation doesn't publish it. Check Settings → Publisher → Listings.",
        404);

    public static EconomyException NotForSale(string templateName) => new(
        "TEMPLATE_NOT_FOR_SALE",
        $"'{templateName}' is free — install it directly from the gallery.",
        409);

    public static EconomyException NotYetApproved(string templateName) => new(
        "TEMPLATE_NOT_APPROVED",
        $"'{templateName}' is still in review, so it can't be bought yet. Try again once it appears in the gallery.",
        409);

    public static EconomyException AlreadyOwned(string templateName) => new(
        "TEMPLATE_ALREADY_OWNED",
        $"This workspace already owns '{templateName}'. Install it from the gallery.",
        409);

    public static EconomyException SellerUnavailable(string templateName) => new(
        "TEMPLATE_SELLER_UNAVAILABLE",
        $"'{templateName}' can't be bought right now because its publisher's payout account isn't active. " +
        "Try again later or contact the publisher.",
        409);

    public static EconomyException PurchaseNotFound(Guid id) => new(
        "PURCHASE_NOT_FOUND",
        $"Purchase '{id}' was not found in this workspace. Refresh the Purchases list.",
        404);

    public static EconomyException NotCancellable() => new(
        "PURCHASE_NOT_CANCELLABLE",
        "Only active monthly subscriptions can be cancelled. One-time purchases don't renew, so there's nothing to cancel.",
        409);

    public static EconomyException NoPublisherAccount() => new(
        "PUBLISHER_NOT_CONNECTED",
        "Your organisation hasn't connected a payout account yet. Open Settings → Publisher and click 'Connect with Stripe'.",
        404);
}
