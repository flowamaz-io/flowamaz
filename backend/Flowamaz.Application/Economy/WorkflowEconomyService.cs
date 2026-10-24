using Flowamaz.Core.Entities.Economy;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Economy;

/// <summary>
/// Workflow Economy (FUNCTIONAL.md §8.9). Publishers connect a Stripe Express account and price their
/// community templates (one-time or monthly). Buyers pay through Checkout as a destination charge: the
/// platform keeps <see cref="PlatformFeePercent"/> (20%) as the application fee and Stripe transfers the
/// rest to the publisher. Webhooks activate entitlements and append to the earnings ledger (idempotent
/// per Stripe object). The publisher's own organisation always gets its templates free.
/// </summary>
public sealed class WorkflowEconomyService : IWorkflowEconomyService
{
    public const string MetadataKind = "kind";
    public const string MetadataKindValue = "template_purchase";
    public const string MetadataPurchaseId = "purchaseId";

    private readonly IEconomyRepository _economy;
    private readonly IWorkflowTemplateRepository _templates;
    private readonly IOrganisationRepository _organisations;
    private readonly IStripeConnectGateway _stripe;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WorkflowEconomyService> _logger;
    private readonly IAuditService? _audit;

    public WorkflowEconomyService(
        IEconomyRepository economy,
        IWorkflowTemplateRepository templates,
        IOrganisationRepository organisations,
        IStripeConnectGateway stripe,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<WorkflowEconomyService> logger,
        IAuditService? audit = null)
    {
        _economy = economy;
        _templates = templates;
        _organisations = organisations;
        _stripe = stripe;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _audit = audit;
        PlatformFeePercent = Math.Clamp(configuration.GetValue("Economy:PlatformFeePercent", 20m), 0m, 100m);
    }

    public decimal PlatformFeePercent { get; }

    // ── Publisher ────────────────────────────────────────────────────────────

    public async Task<PublisherStatus> GetPublisherStatusAsync(Guid orgId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowEconomyService.GetPublisherStatusAsync enter org={OrgId}", orgId);
        var account = await _economy.GetPublisherByOrgAsync(orgId, ct);
        if (account is null)
            return new PublisherStatus(false, false, false, false, [], null, false, PlatformFeePercent);

        // Refresh from Stripe at most once a minute (webhooks keep it current in between).
        if (_stripe.IsConfigured && (account.StatusRefreshedAt is null || account.StatusRefreshedAt < DateTime.UtcNow.AddMinutes(-1)))
        {
            try
            {
                Apply(account, await _stripe.GetAccountStatusAsync(account.StripeAccountId, ct));
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "WorkflowEconomyService could not refresh publisher status org={OrgId}; using stored flags", orgId);
            }
        }

        _logger.LogInformation("WorkflowEconomyService.GetPublisherStatusAsync exit org={OrgId} charges={Charges}", orgId, account.ChargesEnabled);
        return ToStatus(account);
    }

    public async Task<string> StartOnboardingAsync(Guid orgId, string returnUrl, string refreshUrl, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowEconomyService.StartOnboardingAsync enter org={OrgId}", orgId);
        if (!_stripe.IsConfigured) throw EconomyException.NotConfigured();

        var account = await _economy.GetPublisherByOrgAsync(orgId, ct);
        if (account is null)
        {
            var org = await _organisations.GetByIdAsync(orgId, ct) ?? throw EconomyException.NoPublisherAccount();
            var accountId = await _stripe.CreateExpressAccountAsync(orgId, org.BillingEmail, ct);
            account = new PublisherAccount { OrgId = orgId, StripeAccountId = accountId };
            await _economy.AddPublisherAsync(account, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            Audit(orgId, null, "economy.publisher_connected", "publisher_account", account.Id, null);
        }

        var url = await _stripe.CreateOnboardingLinkAsync(account.StripeAccountId, returnUrl, refreshUrl, ct);
        _logger.LogInformation("WorkflowEconomyService.StartOnboardingAsync exit org={OrgId}", orgId);
        return url;
    }

    public async Task<string> CreateDashboardLinkAsync(Guid orgId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowEconomyService.CreateDashboardLinkAsync enter org={OrgId}", orgId);
        if (!_stripe.IsConfigured) throw EconomyException.NotConfigured();
        var account = await _economy.GetPublisherByOrgAsync(orgId, ct) ?? throw EconomyException.NoPublisherAccount();
        if (!account.DetailsSubmitted) throw EconomyException.PublisherNotReady();
        return await _stripe.CreateDashboardLinkAsync(account.StripeAccountId, ct);
    }

    public async Task<PublisherEarningsSummary> GetEarningsAsync(Guid orgId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowEconomyService.GetEarningsAsync enter org={OrgId}", orgId);
        var (gross, fee, net, sales) = await _economy.SumEarningsAsync(orgId, ct);
        var active = await _economy.CountActiveSubscriptionsAsync(orgId, ct);
        var byTemplate = await _economy.EarningsByTemplateAsync(orgId, ct);
        var templates = await _economy.ListTemplatesByOrgAsync(orgId, ct);
        var entries = await _economy.ListEarningsAsync(orgId, 200, ct);
        var names = templates.ToDictionary(t => t.Id, t => t.Name);
        foreach (var missing in (await _economy.TemplateNamesAsync(entries.Select(e => e.TemplateId).Where(id => !names.ContainsKey(id)), ct)))
            names[missing.Key] = missing.Value;

        var listings = templates.Select(t =>
        {
            byTemplate.TryGetValue(t.Id, out var e);
            return ToListing(t, e.Sales, e.Net);
        }).ToList();

        var currency = templates.FirstOrDefault(t => TemplatePricing.IsPaid(t.PricingModel))?.Currency ?? "usd";
        _logger.LogInformation("WorkflowEconomyService.GetEarningsAsync exit org={OrgId} entries={Count}", orgId, entries.Count);
        return new PublisherEarningsSummary(currency, gross, fee, net, sales, active, listings,
            entries.Select(e => new PublisherEarningEntry(e.Id, e.TemplateId, names.GetValueOrDefault(e.TemplateId, "Removed template"),
                e.Type, e.GrossCents, e.FeeCents, e.NetCents, e.Currency, e.OccurredAt)).ToList());
    }

    public async Task ValidatePricingAsync(Guid orgId, TemplatePricingRequest pricing, CancellationToken ct = default)
    {
        var model = TemplatePricing.Normalize(pricing.PricingModel);
        if (!TemplatePricing.IsPaid(model)) return;

        var currency = (pricing.Currency ?? string.Empty).Trim().ToLowerInvariant();
        if (!TemplatePricing.Currencies.Contains(currency))
            throw EconomyException.InvalidPrice(
                $"Currency '{pricing.Currency}' isn't supported. Use one of: {string.Join(", ", TemplatePricing.Currencies.Order())}.");
        if (pricing.PriceCents is < TemplatePricing.MinPriceCents or > TemplatePricing.MaxPriceCents)
            throw EconomyException.InvalidPrice(
                $"Set a price between {TemplatePricing.MinPriceCents / 100m:0.00} and {TemplatePricing.MaxPriceCents / 100m:0.00} {currency.ToUpperInvariant()}.");

        var account = await _economy.GetPublisherByOrgAsync(orgId, ct);
        if (account is not { ChargesEnabled: true }) throw EconomyException.PublisherNotReady();
    }

    public async Task<PublisherListing> SetPricingAsync(Guid orgId, Guid templateId, TemplatePricingRequest pricing, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowEconomyService.SetPricingAsync enter org={OrgId} template={TemplateId}", orgId, templateId);
        var template = await _economy.GetOwnedTemplateAsync(orgId, templateId, ct) ?? throw EconomyException.TemplateNotFound(templateId);
        await ValidatePricingAsync(orgId, pricing, ct);
        ApplyPricing(template, pricing);
        await _unitOfWork.SaveChangesAsync(ct);

        Audit(orgId, null, "economy.listing_priced", "workflow_template", template.Id, template.Name,
            new { template.PricingModel, template.PriceCents, template.Currency });
        var stats = (await _economy.EarningsByTemplateAsync(orgId, ct)).GetValueOrDefault(template.Id);
        _logger.LogInformation("WorkflowEconomyService.SetPricingAsync exit template={TemplateId} model={Model}", templateId, template.PricingModel);
        return ToListing(template, stats.Sales, stats.Net);
    }

    /// <summary>Copies a validated price onto a template (free resets the amount).</summary>
    public static void ApplyPricing(WorkflowTemplate template, TemplatePricingRequest pricing)
    {
        var model = TemplatePricing.Normalize(pricing.PricingModel);
        template.PricingModel = model;
        template.PriceCents = TemplatePricing.IsPaid(model) ? pricing.PriceCents : 0;
        template.Currency = TemplatePricing.IsPaid(model) ? pricing.Currency.Trim().ToLowerInvariant() : "usd";
        template.UpdatedAt = DateTime.UtcNow;
    }

    // ── Buying ───────────────────────────────────────────────────────────────

    public async Task<bool> HasEntitlementAsync(Guid workspaceId, Guid orgId, WorkflowTemplate template, CancellationToken ct = default)
    {
        if (!TemplatePricing.IsPaid(template.PricingModel) || template.OrgId == orgId) return true;
        var purchases = await _economy.ListPurchasesAsync(workspaceId, template.Id, ct);
        return purchases.Any(Grants);
    }

    public async Task<TemplateAccess> GetAccessAsync(Guid workspaceId, Guid orgId, WorkflowTemplate template, CancellationToken ct = default)
    {
        if (!TemplatePricing.IsPaid(template.PricingModel) || template.OrgId == orgId)
            return new TemplateAccess(false, true, "none", null, false);

        var purchases = await _economy.ListPurchasesAsync(workspaceId, template.Id, ct);
        var best = purchases.FirstOrDefault(Grants)
            ?? purchases.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        return new TemplateAccess(true, best is not null && Grants(best), best?.Status ?? "none",
            best?.CurrentPeriodEnd, best?.CancelAtPeriodEnd ?? false);
    }

    public async Task<string> StartPurchaseAsync(
        Guid workspaceId, Guid orgId, Guid userId, Guid templateId, string successUrl, string cancelUrl, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowEconomyService.StartPurchaseAsync enter workspace={WorkspaceId} template={TemplateId}", workspaceId, templateId);
        if (!_stripe.IsConfigured) throw EconomyException.NotConfigured();

        var template = await _templates.GetByIdAsync(templateId, ct) ?? throw EconomyException.TemplateNotFound(templateId);
        if (!TemplatePricing.IsPaid(template.PricingModel)) throw EconomyException.NotForSale(template.Name);
        if (template.ReviewStatus != "approved") throw EconomyException.NotYetApproved(template.Name);
        if (await HasEntitlementAsync(workspaceId, orgId, template, ct)) throw EconomyException.AlreadyOwned(template.Name);

        var seller = template.OrgId is { } publisherOrg ? await _economy.GetPublisherByOrgAsync(publisherOrg, ct) : null;
        if (seller is not { ChargesEnabled: true }) throw EconomyException.SellerUnavailable(template.Name);

        var buyer = await _organisations.GetByIdAsync(orgId, ct);
        var fee = TemplatePricing.Fee(template.PriceCents, PlatformFeePercent);
        var purchase = new TemplatePurchase
        {
            WorkspaceId = workspaceId,
            OrgId = orgId,
            TemplateId = template.Id,
            PublisherOrgId = seller.OrgId,
            PricingModel = template.PricingModel,
            AmountCents = template.PriceCents,
            FeeCents = fee,
            Currency = template.Currency,
            Status = TemplatePurchaseStatus.Pending,
            PurchasedByUserId = userId,
            CreatedBy = userId,
        };
        await _economy.AddPurchaseAsync(purchase, ct);

        var session = await _stripe.CreateTemplateCheckoutAsync(new TemplateCheckoutRequest(
            purchase.Id, template.Id, workspaceId, template.Name, template.PricingModel == TemplatePricing.Monthly,
            template.PriceCents, template.Currency, fee, PlatformFeePercent, seller.StripeAccountId,
            buyer?.BillingEmail, successUrl, cancelUrl), ct);

        purchase.StripeCheckoutSessionId = session.SessionId;
        await _unitOfWork.SaveChangesAsync(ct);

        Audit(orgId, workspaceId, "economy.purchase_started", "workflow_template", template.Id, template.Name, new { purchaseId = purchase.Id }, userId);
        _logger.LogInformation("WorkflowEconomyService.StartPurchaseAsync exit purchase={PurchaseId}", purchase.Id);
        return session.Url;
    }

    public async Task<IReadOnlyList<TemplatePurchaseItem>> ListPurchasesAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var purchases = (await _economy.ListPurchasesAsync(workspaceId, null, ct))
            .Where(p => p.Status != TemplatePurchaseStatus.Pending || p.CreatedAt > DateTime.UtcNow.AddDays(-1))
            .ToList();
        var names = await _economy.TemplateNamesAsync(purchases.Select(p => p.TemplateId).Distinct(), ct);
        return purchases.Select(p => ToItem(p, names.GetValueOrDefault(p.TemplateId, "Removed template"))).ToList();
    }

    public async Task<TemplatePurchaseItem> CancelPurchaseAsync(Guid workspaceId, Guid purchaseId, CancellationToken ct = default)
    {
        _logger.LogInformation("WorkflowEconomyService.CancelPurchaseAsync enter workspace={WorkspaceId} purchase={PurchaseId}", workspaceId, purchaseId);
        var purchase = await _economy.GetPurchaseAsync(workspaceId, purchaseId, ct) ?? throw EconomyException.PurchaseNotFound(purchaseId);
        if (purchase.PricingModel != TemplatePricing.Monthly || string.IsNullOrEmpty(purchase.StripeSubscriptionId)
            || purchase.Status is not (TemplatePurchaseStatus.Active or TemplatePurchaseStatus.PastDue))
            throw EconomyException.NotCancellable();
        if (!_stripe.IsConfigured) throw EconomyException.NotConfigured();

        await _stripe.CancelSubscriptionAtPeriodEndAsync(purchase.StripeSubscriptionId, ct);
        purchase.CancelAtPeriodEnd = true;
        purchase.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        var names = await _economy.TemplateNamesAsync([purchase.TemplateId], ct);
        Audit(purchase.OrgId, workspaceId, "economy.subscription_cancel_requested", "template_purchase", purchase.Id, null);
        return ToItem(purchase, names.GetValueOrDefault(purchase.TemplateId, "Removed template"));
    }

    // ── Webhooks ─────────────────────────────────────────────────────────────

    public async Task<bool> HandleCheckoutCompletedAsync(EconomyCheckoutCompleted e, CancellationToken ct = default)
    {
        if (!e.Metadata.TryGetValue(MetadataKind, out var kind) || kind != MetadataKindValue) return false;
        if (!e.Metadata.TryGetValue(MetadataPurchaseId, out var raw) || !Guid.TryParse(raw, out var purchaseId))
        {
            _logger.LogWarning("Economy checkout.session.completed without a purchase id — ignored");
            return true;
        }

        var purchase = await _economy.GetPurchaseForWebhookAsync(purchaseId, ct);
        if (purchase is null)
        {
            _logger.LogWarning("Economy checkout.session.completed for unknown purchase={PurchaseId}", purchaseId);
            return true;
        }
        if (e.PaymentStatus is not ("paid" or "no_payment_required"))
        {
            _logger.LogInformation("Economy checkout completed but unpaid ({Status}) purchase={PurchaseId} — waiting", e.PaymentStatus, purchaseId);
            return true;
        }

        var now = DateTime.UtcNow;
        purchase.Status = TemplatePurchaseStatus.Active;
        purchase.PurchasedAt ??= now;
        purchase.StripeCheckoutSessionId ??= e.SessionId;
        purchase.StripePaymentIntentId ??= e.PaymentIntentId;
        purchase.StripeSubscriptionId ??= e.SubscriptionId;
        if (purchase.PricingModel == TemplatePricing.Monthly) purchase.CurrentPeriodEnd ??= now.AddMonths(1);
        purchase.UpdatedAt = now;

        var gross = (int)(e.AmountTotal ?? purchase.AmountCents);
        await AddEarningAsync(purchase, "sale", gross, purchase.FeeCents, e.SessionId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        Audit(purchase.OrgId, purchase.WorkspaceId, "economy.purchase_completed", "template_purchase", purchase.Id, null,
            new { purchase.TemplateId, purchase.AmountCents, purchase.Currency });
        _logger.LogInformation("Economy purchase activated purchase={PurchaseId} template={TemplateId}", purchase.Id, purchase.TemplateId);
        return true;
    }

    public async Task<bool> HandleInvoicePaidAsync(EconomyInvoicePaid e, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(e.SubscriptionId)) return false;
        var purchase = await _economy.GetPurchaseBySubscriptionAsync(e.SubscriptionId, ct);
        if (purchase is null) return false;

        purchase.Status = TemplatePurchaseStatus.Active;
        if (e.PeriodEnd is { } end && end > (purchase.CurrentPeriodEnd ?? DateTime.MinValue)) purchase.CurrentPeriodEnd = end;
        else if (e.PeriodEnd is null && e.BillingReason == "subscription_cycle")
            purchase.CurrentPeriodEnd = (purchase.CurrentPeriodEnd is { } cur && cur > DateTime.UtcNow ? cur : DateTime.UtcNow).AddMonths(1);
        purchase.UpdatedAt = DateTime.UtcNow;

        // The first invoice is the sale already recorded at checkout; only renewals add a ledger row.
        if (e.BillingReason is not "subscription_create" && e.AmountPaid > 0)
        {
            var gross = (int)e.AmountPaid;
            await AddEarningAsync(purchase, "renewal", gross, TemplatePricing.Fee(gross, PlatformFeePercent), e.InvoiceId, ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Economy invoice paid purchase={PurchaseId} reason={Reason}", purchase.Id, e.BillingReason);
        return true;
    }

    public async Task<bool> HandleInvoicePaymentFailedAsync(string? subscriptionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(subscriptionId)) return false;
        var purchase = await _economy.GetPurchaseBySubscriptionAsync(subscriptionId, ct);
        if (purchase is null) return false;

        // Past due keeps access while Stripe retries (smart retries), then subscription.deleted cancels it.
        purchase.Status = TemplatePurchaseStatus.PastDue;
        purchase.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Economy invoice payment failed purchase={PurchaseId}", purchase.Id);
        return true;
    }

    public async Task<bool> HandleSubscriptionChangedAsync(EconomySubscriptionChanged e, CancellationToken ct = default)
    {
        var purchase = await _economy.GetPurchaseBySubscriptionAsync(e.SubscriptionId, ct);
        if (purchase is null) return false;

        if (purchase.Status != TemplatePurchaseStatus.Refunded)
        {
            purchase.Status = e.Deleted ? TemplatePurchaseStatus.Canceled : e.Status switch
            {
                "active" or "trialing" => TemplatePurchaseStatus.Active,
                "past_due" or "unpaid" => TemplatePurchaseStatus.PastDue,
                "canceled" or "incomplete_expired" => TemplatePurchaseStatus.Canceled,
                _ => purchase.Status,
            };
        }
        purchase.CancelAtPeriodEnd = !e.Deleted && e.CancelAtPeriodEnd;
        if (e.CurrentPeriodEnd is { } end) purchase.CurrentPeriodEnd = end;
        purchase.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Economy subscription change purchase={PurchaseId} status={Status}", purchase.Id, purchase.Status);
        return true;
    }

    public async Task<bool> HandleChargeRefundedAsync(EconomyChargeRefunded e, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(e.PaymentIntentId)) return false;
        var purchase = await _economy.GetPurchaseByPaymentIntentAsync(e.PaymentIntentId, ct);
        if (purchase is null) return false;

        // Refunds are issued with refund_application_fee + reverse_transfer, so the fee and the
        // publisher's share reverse proportionally; record each refund once.
        foreach (var refund in e.Refunds)
        {
            var gross = (int)refund.Amount;
            var fee = e.Amount > 0 ? (int)Math.Round((decimal)purchase.FeeCents * refund.Amount / e.Amount, MidpointRounding.AwayFromZero) : 0;
            await AddEarningAsync(purchase, "refund", -gross, -fee, refund.RefundId, ct);
        }
        if (e.AmountRefunded >= e.Amount) purchase.Status = TemplatePurchaseStatus.Refunded;
        purchase.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        Audit(purchase.OrgId, purchase.WorkspaceId, "economy.purchase_refunded", "template_purchase", purchase.Id, null,
            new { amountRefunded = e.AmountRefunded });
        return true;
    }

    public async Task HandleAccountUpdatedAsync(ConnectAccountStatus status, CancellationToken ct = default)
    {
        var account = await _economy.GetPublisherByStripeAccountAsync(status.AccountId, ct);
        if (account is null)
        {
            _logger.LogInformation("Economy account.updated for an account we don't track — ignored");
            return;
        }
        Apply(account, status);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Economy publisher status org={OrgId} charges={Charges} payouts={Payouts}",
            account.OrgId, account.ChargesEnabled, account.PayoutsEnabled);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool Grants(TemplatePurchase p) => p.Status is TemplatePurchaseStatus.Active or TemplatePurchaseStatus.PastDue;

    private async Task AddEarningAsync(TemplatePurchase purchase, string type, int gross, int fee, string stripeObjectId, CancellationToken ct)
    {
        if (await _economy.EarningExistsAsync(stripeObjectId, type, ct)) return;
        await _economy.AddEarningAsync(new PublisherEarning
        {
            PublisherOrgId = purchase.PublisherOrgId,
            PurchaseId = purchase.Id,
            TemplateId = purchase.TemplateId,
            Type = type,
            GrossCents = gross,
            FeeCents = fee,
            NetCents = gross - fee,
            Currency = purchase.Currency,
            StripeObjectId = stripeObjectId,
            OccurredAt = DateTime.UtcNow,
        }, ct);
    }

    private static void Apply(PublisherAccount account, ConnectAccountStatus status)
    {
        account.ChargesEnabled = status.ChargesEnabled;
        account.PayoutsEnabled = status.PayoutsEnabled;
        account.DetailsSubmitted = status.DetailsSubmitted;
        account.Country = status.Country;
        account.RequirementsDue = status.RequirementsDue.ToArray();
        account.StatusRefreshedAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;
    }

    private PublisherStatus ToStatus(PublisherAccount a) =>
        new(true, a.ChargesEnabled, a.PayoutsEnabled, a.DetailsSubmitted, a.RequirementsDue, a.Country, a.ChargesEnabled, PlatformFeePercent);

    private static PublisherListing ToListing(WorkflowTemplate t, int sales, int net) =>
        new(t.Id, t.Name, t.PricingModel, t.PriceCents, t.Currency, t.ReviewStatus, t.InstallCount, sales, net);

    private static TemplatePurchaseItem ToItem(TemplatePurchase p, string name) =>
        new(p.Id, p.TemplateId, name, p.PricingModel, p.AmountCents, p.Currency, p.Status, p.PurchasedAt, p.CurrentPeriodEnd, p.CancelAtPeriodEnd);

    private void Audit(Guid orgId, Guid? workspaceId, string eventType, string resourceType, Guid resourceId, string? label,
        object? metadata = null, Guid? userId = null) =>
        _audit?.RecordAsync(new AuditEventRequest
        {
            OrgId = orgId,
            WorkspaceId = workspaceId,
            ActorUserId = userId,
            ActorType = userId is null ? "system" : "user",
            EventType = eventType,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ResourceLabel = label,
            Action = "updated",
            Metadata = metadata,
        });
}
