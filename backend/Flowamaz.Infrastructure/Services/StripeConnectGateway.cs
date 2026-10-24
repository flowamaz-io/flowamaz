using Flowamaz.Core.Configuration;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Stripe Connect over Stripe.net for the Workflow Economy. Express accounts for publishers;
/// destination charges (Checkout) with an application fee for buyers. Works with test keys
/// (sk_test_…) unchanged. Account ids are logged only as "acct_…" prefixes — never secrets.
/// </summary>
public sealed class StripeConnectGateway : IStripeConnectGateway
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripeConnectGateway> _logger;

    public StripeConnectGateway(IOptions<StripeOptions> options, ILogger<StripeConnectGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    private RequestOptions Request => new() { ApiKey = _options.SecretKey };

    public async Task<string> CreateExpressAccountAsync(Guid orgId, string email, CancellationToken ct = default)
    {
        _logger.LogInformation("StripeConnectGateway.CreateExpressAccountAsync enter org={OrgId}", orgId);
        var account = await new AccountService().CreateAsync(new AccountCreateOptions
        {
            Type = "express",
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            Capabilities = new AccountCapabilitiesOptions
            {
                CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
                Transfers = new AccountCapabilitiesTransfersOptions { Requested = true },
            },
            Metadata = new Dictionary<string, string> { ["orgId"] = orgId.ToString() },
        }, Request, ct);
        _logger.LogInformation("StripeConnectGateway.CreateExpressAccountAsync exit org={OrgId}", orgId);
        return account.Id;
    }

    public async Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct = default)
    {
        var link = await new AccountLinkService().CreateAsync(new AccountLinkCreateOptions
        {
            Account = accountId,
            ReturnUrl = returnUrl,
            RefreshUrl = refreshUrl,
            Type = "account_onboarding",
        }, Request, ct);
        return link.Url;
    }

    public async Task<string> CreateDashboardLinkAsync(string accountId, CancellationToken ct = default)
    {
        var link = await new AccountLoginLinkService().CreateAsync(accountId, new AccountLoginLinkCreateOptions(), Request, ct);
        return link.Url;
    }

    public async Task<ConnectAccountStatus> GetAccountStatusAsync(string accountId, CancellationToken ct = default)
    {
        var account = await new AccountService().GetAsync(accountId, null, Request, ct);
        return ToStatus(account);
    }

    public static ConnectAccountStatus ToStatus(Account account) => new(
        account.Id,
        account.ChargesEnabled,
        account.PayoutsEnabled,
        account.DetailsSubmitted,
        account.Country,
        (IReadOnlyList<string>?)account.Requirements?.CurrentlyDue ?? []);

    public async Task<TemplateCheckoutSession> CreateTemplateCheckoutAsync(TemplateCheckoutRequest r, CancellationToken ct = default)
    {
        _logger.LogInformation("StripeConnectGateway.CreateTemplateCheckoutAsync enter purchase={PurchaseId} monthly={Monthly}", r.PurchaseId, r.Monthly);
        var metadata = new Dictionary<string, string>
        {
            ["kind"] = "template_purchase",
            ["purchaseId"] = r.PurchaseId.ToString(),
            ["templateId"] = r.TemplateId.ToString(),
            ["workspaceId"] = r.WorkspaceId.ToString(),
        };

        var price = new SessionLineItemPriceDataOptions
        {
            Currency = r.Currency,
            UnitAmount = r.AmountCents,
            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = $"Workflow: {r.TemplateName}" },
            Recurring = r.Monthly ? new SessionLineItemPriceDataRecurringOptions { Interval = "month" } : null,
        };

        var options = new SessionCreateOptions
        {
            Mode = r.Monthly ? "subscription" : "payment",
            SuccessUrl = r.SuccessUrl,
            CancelUrl = r.CancelUrl,
            CustomerEmail = string.IsNullOrWhiteSpace(r.CustomerEmail) ? null : r.CustomerEmail,
            ClientReferenceId = r.PurchaseId.ToString(),
            LineItems = [new SessionLineItemOptions { PriceData = price, Quantity = 1 }],
            Metadata = metadata,
        };

        if (r.Monthly)
        {
            options.SubscriptionData = new SessionSubscriptionDataOptions
            {
                ApplicationFeePercent = r.FeePercent,
                TransferData = new SessionSubscriptionDataTransferDataOptions { Destination = r.DestinationAccountId },
                Metadata = metadata,
            };
        }
        else
        {
            options.PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                ApplicationFeeAmount = r.FeeCents,
                TransferData = new SessionPaymentIntentDataTransferDataOptions { Destination = r.DestinationAccountId },
                Metadata = metadata,
            };
        }

        var session = await new SessionService().CreateAsync(options, Request, ct);
        _logger.LogInformation("StripeConnectGateway.CreateTemplateCheckoutAsync exit purchase={PurchaseId}", r.PurchaseId);
        return new TemplateCheckoutSession(session.Id, session.Url);
    }

    public async Task CancelSubscriptionAtPeriodEndAsync(string subscriptionId, CancellationToken ct = default) =>
        await new SubscriptionService().UpdateAsync(subscriptionId,
            new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, Request, ct);
}
