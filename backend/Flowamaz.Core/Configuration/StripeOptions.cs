namespace Flowamaz.Core.Configuration;

/// <summary>
/// Stripe billing configuration (prompt 07-01). Binds the "Stripe" section; secrets are sourced
/// from environment variables (STRIPE_SECRET_KEY, STRIPE_WEBHOOK_SECRET, STRIPE_*_PRICE_ID_*).
/// When <see cref="SecretKey"/> is empty (Development/tests) the StripeService degrades gracefully
/// and never reaches the Stripe network.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    public string StarterPriceIdMonthly { get; set; } = string.Empty;
    public string StarterPriceIdAnnual { get; set; } = string.Empty;
    public string ProPriceIdMonthly { get; set; } = string.Empty;
    public string ProPriceIdAnnual { get; set; } = string.Empty;

    /// <summary>True when a real secret key is configured — guards every Stripe network call.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
