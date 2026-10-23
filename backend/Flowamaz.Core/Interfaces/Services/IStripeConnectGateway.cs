using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Stripe Connect calls for the Workflow Economy. Implemented in Infrastructure over Stripe.net so the
/// economy rules stay testable without the network. Never returns or logs secrets.
/// </summary>
public interface IStripeConnectGateway
{
    bool IsConfigured { get; }
    Task<string> CreateExpressAccountAsync(Guid orgId, string email, CancellationToken ct = default);
    Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct = default);
    Task<string> CreateDashboardLinkAsync(string accountId, CancellationToken ct = default);
    Task<ConnectAccountStatus> GetAccountStatusAsync(string accountId, CancellationToken ct = default);
    Task<TemplateCheckoutSession> CreateTemplateCheckoutAsync(TemplateCheckoutRequest request, CancellationToken ct = default);
    Task CancelSubscriptionAtPeriodEndAsync(string subscriptionId, CancellationToken ct = default);
}
