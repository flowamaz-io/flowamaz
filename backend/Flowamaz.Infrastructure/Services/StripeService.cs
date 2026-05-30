using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Stripe;
using Stripe.Checkout;
using StripeSubscription = Stripe.Subscription;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// Stripe billing integration (prompt 07-01). Checkout + Customer Portal + webhook lifecycle.
/// Constructs without a real key (Development/tests): network calls are guarded by
/// <see cref="StripeOptions.IsConfigured"/> and webhook signature verification is wrapped so the
/// dispatch logic is unit-testable. Stripe secrets/customer ids are never logged or returned to
/// the frontend. Webhooks are idempotent via a Redis processed-event-id set.
/// </summary>
public sealed class StripeService : IStripeService
{
    private static readonly TimeSpan ProcessedEventTtl = TimeSpan.FromDays(7);

    private readonly StripeOptions _options;
    private readonly IOrganisationRepository _organisations;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConnectionMultiplexer _redis;
    private readonly IEmailService _email;
    private readonly ILogger<StripeService> _logger;
    private readonly IStripeEventVerifier _verifier;
    private readonly IAuditService? _audit;

    public StripeService(
        IOptions<StripeOptions> options,
        IOrganisationRepository organisations,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IConnectionMultiplexer redis,
        IEmailService email,
        ILogger<StripeService> logger,
        IStripeEventVerifier? verifier = null,
        IAuditService? audit = null)
    {
        _options = options.Value;
        _organisations = organisations;
        _subscriptions = subscriptions;
        _plans = plans;
        _unitOfWork = unitOfWork;
        _redis = redis;
        _email = email;
        _logger = logger;
        _verifier = verifier ?? new StripeEventVerifier();
        _audit = audit;

        // Stripe.net reads ApiKey from this static; only set it when a real key is configured so the
        // app starts (and tests run) with no network/credential coupling.
        if (_options.IsConfigured)
            StripeConfiguration.ApiKey = _options.SecretKey;
    }

    public async Task<string> CreateCheckoutSessionAsync(
        Guid orgId, Guid planId, bool annual, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "StripeService.CreateCheckoutSessionAsync enter org={OrgId} plan={PlanId} annual={Annual}", orgId, planId, annual);
        try
        {
            if (!_options.IsConfigured) throw BillingException.NotConfigured();

            var org = await _organisations.GetForUpdateAsync(orgId, cancellationToken)
                ?? throw BillingException.NoCustomer();

            var plan = await _plans.GetByIdAsync(planId, cancellationToken)
                ?? throw BillingException.PlanNotFound(planId);

            var priceId = ResolvePriceId(plan, annual);
            if (string.IsNullOrWhiteSpace(priceId)) throw BillingException.PlanNotPurchasable(plan.Name);

            var sessionService = new SessionService();
            var session = await sessionService.CreateAsync(new SessionCreateOptions
            {
                Mode = "subscription",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Customer = string.IsNullOrWhiteSpace(org.StripeCustomerId) ? null : org.StripeCustomerId,
                CustomerEmail = string.IsNullOrWhiteSpace(org.StripeCustomerId) ? org.BillingEmail : null,
                LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
                Metadata = new Dictionary<string, string>
                {
                    ["orgId"] = orgId.ToString(),
                    ["planId"] = planId.ToString(),
                    ["billingCycle"] = annual ? nameof(BillingCycle.Annual) : nameof(BillingCycle.Monthly),
                },
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("StripeService.CreateCheckoutSessionAsync exit org={OrgId} sessionCreated", orgId);
            return session.Url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StripeService.CreateCheckoutSessionAsync error org={OrgId} plan={PlanId}", orgId, planId);
            throw;
        }
    }

    public async Task<string> CreateCustomerPortalSessionAsync(Guid orgId, string returnUrl, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("StripeService.CreateCustomerPortalSessionAsync enter org={OrgId}", orgId);
        try
        {
            if (!_options.IsConfigured) throw BillingException.NotConfigured();

            var org = await _organisations.GetForUpdateAsync(orgId, cancellationToken)
                ?? throw BillingException.NoCustomer();
            if (string.IsNullOrWhiteSpace(org.StripeCustomerId)) throw BillingException.NoCustomer();

            var portalService = new Stripe.BillingPortal.SessionService();
            var session = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = org.StripeCustomerId,
                ReturnUrl = returnUrl,
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("StripeService.CreateCustomerPortalSessionAsync exit org={OrgId} portalCreated", orgId);
            return session.Url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StripeService.CreateCustomerPortalSessionAsync error org={OrgId}", orgId);
            throw;
        }
    }

    public async Task HandleWebhookAsync(string payload, string stripeSignature, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("StripeService.HandleWebhookAsync enter");
        try
        {
            // Signature verification first — reject anything we can't trust (→ 400 via BillingException).
            Event stripeEvent;
            try
            {
                stripeEvent = _verifier.Verify(payload, stripeSignature, _options.WebhookSecret);
            }
            catch (StripeException)
            {
                throw BillingException.InvalidWebhookSignature();
            }

            if (await AlreadyProcessedAsync(stripeEvent.Id))
            {
                _logger.LogInformation("StripeService.HandleWebhookAsync skip duplicate event={EventId} type={Type}",
                    stripeEvent.Id, stripeEvent.Type);
                return;
            }

            switch (stripeEvent.Type)
            {
                case EventTypes.CheckoutSessionCompleted:
                    await HandleCheckoutCompletedAsync((Session)stripeEvent.Data.Object, cancellationToken);
                    break;
                case EventTypes.CustomerSubscriptionUpdated:
                    await HandleSubscriptionUpdatedAsync((StripeSubscription)stripeEvent.Data.Object, cancellationToken);
                    break;
                case EventTypes.CustomerSubscriptionDeleted:
                    await HandleSubscriptionDeletedAsync((StripeSubscription)stripeEvent.Data.Object, cancellationToken);
                    break;
                case EventTypes.InvoicePaymentFailed:
                    await HandlePaymentFailedAsync((Invoice)stripeEvent.Data.Object, cancellationToken);
                    break;
                case EventTypes.InvoicePaymentSucceeded:
                    await HandlePaymentSucceededAsync((Invoice)stripeEvent.Data.Object, cancellationToken);
                    break;
                default:
                    _logger.LogInformation("StripeService.HandleWebhookAsync ignored event type={Type}", stripeEvent.Type);
                    break;
            }

            await MarkProcessedAsync(stripeEvent.Id);
            _logger.LogInformation("StripeService.HandleWebhookAsync exit event={EventId} type={Type}", stripeEvent.Id, stripeEvent.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StripeService.HandleWebhookAsync error");
            throw;
        }
    }

    // ── Webhook handlers ──────────────────────────────────────────────────────

    private async Task HandleCheckoutCompletedAsync(Session session, CancellationToken ct)
    {
        if (!TryGetGuid(session.Metadata, "orgId", out var orgId) ||
            !TryGetGuid(session.Metadata, "planId", out var planId))
        {
            _logger.LogWarning("StripeService checkout.session.completed missing metadata — session ignored");
            return;
        }

        var cycle = session.Metadata is not null
            && session.Metadata.TryGetValue("billingCycle", out var c)
            && string.Equals(c, nameof(BillingCycle.Annual), StringComparison.OrdinalIgnoreCase)
            ? BillingCycle.Annual : BillingCycle.Monthly;

        var org = await _organisations.GetForUpdateAsync(orgId, ct);
        if (org is null)
        {
            _logger.LogWarning("StripeService checkout.session.completed unknown org={OrgId}", orgId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.CustomerId)) org.StripeCustomerId = session.CustomerId;
        org.PlanId = planId;
        org.Status = OrgStatus.Active;
        org.UpdatedAt = DateTime.UtcNow;

        var sub = await _subscriptions.GetByOrgIdAsync(orgId, ct);
        var now = DateTime.UtcNow;
        if (sub is null)
        {
            sub = new Core.Entities.Platform.Subscription
            {
                OrgId = orgId,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = now.AddMonths(cycle == BillingCycle.Annual ? 12 : 1),
            };
            await _subscriptions.AddAsync(sub, ct);
        }

        sub.PlanId = planId;
        sub.BillingCycle = cycle;
        sub.Status = SubscriptionStatus.Active;
        sub.StripeSubscriptionId = session.SubscriptionId;
        sub.TrialEndsAt = null;
        sub.CanceledAt = null;
        sub.PaymentFailed = false;
        sub.UpdatedAt = now;

        await _unitOfWork.SaveChangesAsync(ct);

        // Audit: org-level billing event. No Stripe secrets/ids in metadata.
        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            OrgId = orgId,
            ActorType = "system",
            EventType = "billing.plan_upgraded",
            ResourceType = "billing",
            ResourceId = orgId,
            Action = "updated",
            Metadata = new { planId },
        }, ct);

        _logger.LogInformation("StripeService checkout.session.completed activated org={OrgId} plan={PlanId}", orgId, planId);
    }

    private async Task HandleSubscriptionUpdatedAsync(StripeSubscription stripeSub, CancellationToken ct)
    {
        var sub = await ResolveSubscriptionAsync(stripeSub, ct);
        if (sub is null) return;

        sub.Status = MapStatus(stripeSub.Status);
        if (stripeSub.CanceledAt is { } canceledAt) sub.CanceledAt = canceledAt;
        sub.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("StripeService customer.subscription.updated org={OrgId} status={Status}", sub.OrgId, sub.Status);
    }

    private async Task HandleSubscriptionDeletedAsync(StripeSubscription stripeSub, CancellationToken ct)
    {
        var sub = await ResolveSubscriptionAsync(stripeSub, ct);
        if (sub is null) return;

        var org = await _organisations.GetForUpdateAsync(sub.OrgId, ct);
        if (org is not null)
        {
            // Downgrade to Community on cancellation — EditionService re-reads the new plan limits.
            org.PlanId = PlatformSeedData.CommunityPlanId;
            org.Status = OrgStatus.Active;
            org.UpdatedAt = DateTime.UtcNow;
        }

        sub.PlanId = PlatformSeedData.CommunityPlanId;
        sub.Status = SubscriptionStatus.Cancelled;
        sub.CanceledAt = DateTime.UtcNow;
        sub.StripeSubscriptionId = null;
        sub.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("StripeService customer.subscription.deleted downgraded org={OrgId} to Community", sub.OrgId);
    }

    private async Task HandlePaymentFailedAsync(Invoice invoice, CancellationToken ct)
    {
        var org = await ResolveOrgByCustomerAsync(invoice.CustomerId, ct);
        if (org is null) return;

        var sub = await _subscriptions.GetByOrgIdAsync(org.Id, ct);
        if (sub is not null)
        {
            sub.PaymentFailed = true;
            sub.Status = SubscriptionStatus.PastDue;
            sub.UpdatedAt = DateTime.UtcNow;
        }

        org.Status = OrgStatus.Suspended;
        org.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        await _email.SendAsync(
            org.BillingEmail,
            "Action needed: your Flowamaz payment failed",
            "<p>Your most recent Flowamaz payment did not go through, so your subscription is now past due.</p>" +
            "<p>Update your payment method in <strong>Settings → Billing → Manage billing</strong> to avoid losing access. " +
            "If you need help, reply to this email or contact support@flowamaz.io.</p>",
            "Your most recent Flowamaz payment did not go through. Update your payment method in Settings → Billing → Manage billing.",
            ct);

        _audit?.RecordAsync(new Core.Models.AuditEventRequest
        {
            OrgId = org.Id,
            ActorType = "system",
            EventType = "billing.payment_failed",
            ResourceType = "billing",
            ResourceId = org.Id,
            Action = "updated",
        }, ct);

        _logger.LogInformation("StripeService invoice.payment_failed flagged org={OrgId}", org.Id);
    }

    private async Task HandlePaymentSucceededAsync(Invoice invoice, CancellationToken ct)
    {
        var org = await ResolveOrgByCustomerAsync(invoice.CustomerId, ct);
        if (org is null) return;

        var sub = await _subscriptions.GetByOrgIdAsync(org.Id, ct);
        if (sub is not null && (sub.PaymentFailed || sub.Status == SubscriptionStatus.PastDue))
        {
            sub.PaymentFailed = false;
            sub.Status = SubscriptionStatus.Active;
            sub.UpdatedAt = DateTime.UtcNow;
            org.Status = OrgStatus.Active;
            org.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        _logger.LogInformation("StripeService invoice.payment_succeeded cleared org={OrgId}", org.Id);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Core.Entities.Platform.Subscription?> ResolveSubscriptionAsync(StripeSubscription stripeSub, CancellationToken ct)
    {
        var sub = await _subscriptions.GetByStripeSubscriptionIdAsync(stripeSub.Id, ct);
        if (sub is null)
            _logger.LogWarning("StripeService no local subscription for stripe subscription event");
        return sub;
    }

    private async Task<Organisation?> ResolveOrgByCustomerAsync(string? customerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            _logger.LogWarning("StripeService invoice event missing customer — ignored");
            return null;
        }

        var org = await _organisations.GetByStripeCustomerIdAsync(customerId, ct);
        if (org is null) _logger.LogWarning("StripeService invoice event for unknown customer — ignored");
        return org;
    }

    private string ResolvePriceId(Core.Entities.Platform.Plan plan, bool annual)
    {
        // Plan rows store Stripe price ids (may be null in seed); fall back to env-configured ids by slug.
        var fromPlan = annual ? plan.StripePriceIdAnnual : plan.StripePriceIdMonthly;
        if (!string.IsNullOrWhiteSpace(fromPlan)) return fromPlan;

        return plan.Slug switch
        {
            "starter" => annual ? _options.StarterPriceIdAnnual : _options.StarterPriceIdMonthly,
            "pro" => annual ? _options.ProPriceIdAnnual : _options.ProPriceIdMonthly,
            _ => string.Empty,
        };
    }

    private static SubscriptionStatus MapStatus(string? stripeStatus) => stripeStatus switch
    {
        "active" => SubscriptionStatus.Active,
        "trialing" => SubscriptionStatus.Trialing,
        "past_due" or "unpaid" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" => SubscriptionStatus.Cancelled,
        "paused" => SubscriptionStatus.Paused,
        _ => SubscriptionStatus.Active,
    };

    private static bool TryGetGuid(IDictionary<string, string>? metadata, string key, out Guid value)
    {
        value = Guid.Empty;
        return metadata is not null && metadata.TryGetValue(key, out var raw) && Guid.TryParse(raw, out value);
    }

    private static string ProcessedKey(string eventId) => $"stripe:event:{eventId}";

    private async Task<bool> AlreadyProcessedAsync(string eventId) =>
        await _redis.GetDatabase().KeyExistsAsync(ProcessedKey(eventId));

    private async Task MarkProcessedAsync(string eventId) =>
        await _redis.GetDatabase().StringSetAsync(ProcessedKey(eventId), "1", ProcessedEventTtl);
}

/// <summary>
/// Thin seam over <see cref="EventUtility.ConstructEvent(string, string, string)"/> so the webhook
/// dispatch logic is unit-testable without a real Stripe signature. The default implementation does
/// the real signature verification.
/// </summary>
public interface IStripeEventVerifier
{
    Event Verify(string payload, string signature, string webhookSecret);
}

/// <summary>Real signature verification via Stripe's EventUtility.</summary>
public sealed class StripeEventVerifier : IStripeEventVerifier
{
    public Event Verify(string payload, string signature, string webhookSecret) =>
        EventUtility.ConstructEvent(payload, signature, webhookSecret);
}
