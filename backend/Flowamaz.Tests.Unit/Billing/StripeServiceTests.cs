using FluentAssertions;
using Flowamaz.Core.Configuration;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence.Seed;
using Flowamaz.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Stripe;
using Stripe.Checkout;
using DomainSubscription = Flowamaz.Core.Entities.Platform.Subscription;

namespace Flowamaz.Tests.Unit.Billing;

/// <summary>
/// StripeService webhook dispatch (prompt 07-01). The Stripe signature verification is swapped for a
/// stub <see cref="IStripeEventVerifier"/> so we test the lifecycle handlers without a real signature
/// or any Stripe network call. Redis idempotency is mocked (every event is "new").
/// </summary>
public class StripeServiceTests
{
    private readonly Mock<IOrganisationRepository> _orgs = new();
    private readonly Mock<ISubscriptionRepository> _subs = new();
    private readonly Mock<IPlanRepository> _plans = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IStripeEventVerifier> _verifier = new();

    private static readonly Guid ProPlanId = new("a1000000-0000-0000-0000-000000000003");

    public StripeServiceTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(_db.Object);
        // Every event id is fresh (not yet processed).
        _db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(false);
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
    }

    private StripeService Build(bool configured = true)
    {
        var options = Options.Create(new StripeOptions
        {
            SecretKey = configured ? "sk_test_dummy" : string.Empty,
            WebhookSecret = "whsec_dummy",
            ProPriceIdMonthly = "price_pro_monthly",
        });
        return new StripeService(options, _orgs.Object, _subs.Object, _plans.Object, _uow.Object,
            _redis.Object, _email.Object, NullLogger<StripeService>.Instance, _verifier.Object);
    }

    private void StubEvent(string type, object dataObject)
    {
        var ev = new Event
        {
            Id = $"evt_{Guid.NewGuid():N}",
            Type = type,
            Data = new EventData { Object = (IHasObject)dataObject },
        };
        _verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(ev);
    }

    [Fact]
    public async Task CheckoutSessionCompleted_updates_org_plan()
    {
        var orgId = Guid.NewGuid();
        var org = new Organisation { Id = orgId, PlanId = PlatformSeedData.CommunityPlanId, Status = OrgStatus.Trial };
        _orgs.Setup(o => o.GetForUpdateAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _subs.Setup(s => s.GetByOrgIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync((DomainSubscription?)null);

        StubEvent(EventTypes.CheckoutSessionCompleted, new Session
        {
            CustomerId = "cus_123",
            SubscriptionId = "sub_123",
            Metadata = new Dictionary<string, string>
            {
                ["orgId"] = orgId.ToString(),
                ["planId"] = ProPlanId.ToString(),
                ["billingCycle"] = nameof(BillingCycle.Monthly),
            },
        });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        org.PlanId.Should().Be(ProPlanId);
        org.Status.Should().Be(OrgStatus.Active);
        org.StripeCustomerId.Should().Be("cus_123");
        _subs.Verify(s => s.AddAsync(It.Is<DomainSubscription>(x =>
            x.PlanId == ProPlanId && x.Status == SubscriptionStatus.Active && x.StripeSubscriptionId == "sub_123"),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubscriptionDeleted_downgrades_org_to_community()
    {
        var orgId = Guid.NewGuid();
        var org = new Organisation { Id = orgId, PlanId = ProPlanId, Status = OrgStatus.Active };
        var sub = new DomainSubscription { OrgId = orgId, PlanId = ProPlanId, StripeSubscriptionId = "sub_999", Status = SubscriptionStatus.Active };
        _subs.Setup(s => s.GetByStripeSubscriptionIdAsync("sub_999", It.IsAny<CancellationToken>())).ReturnsAsync(sub);
        _orgs.Setup(o => o.GetForUpdateAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);

        StubEvent(EventTypes.CustomerSubscriptionDeleted, new Stripe.Subscription { Id = "sub_999" });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        org.PlanId.Should().Be(PlatformSeedData.CommunityPlanId);
        sub.Status.Should().Be(SubscriptionStatus.Cancelled);
        sub.PlanId.Should().Be(PlatformSeedData.CommunityPlanId);
        sub.CanceledAt.Should().NotBeNull();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidSignature_throws_billing_exception()
    {
        _verifier
            .Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new StripeException("bad signature"));

        var act = async () => await Build().HandleWebhookAsync("{}", "bad-sig", CancellationToken.None);

        await act.Should().ThrowAsync<BillingException>()
            .Where(e => e.ErrorCode == "STRIPE_WEBHOOK_SIGNATURE_INVALID" && e.HttpStatusCode == 400);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubscriptionUpdated_maps_stripe_status_to_domain_status()
    {
        var orgId = Guid.NewGuid();
        var sub = new DomainSubscription { OrgId = orgId, PlanId = ProPlanId, StripeSubscriptionId = "sub_upd", Status = SubscriptionStatus.Active };
        _subs.Setup(s => s.GetByStripeSubscriptionIdAsync("sub_upd", It.IsAny<CancellationToken>())).ReturnsAsync(sub);

        StubEvent(EventTypes.CustomerSubscriptionUpdated, new Stripe.Subscription { Id = "sub_upd", Status = "past_due" });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        sub.Status.Should().Be(SubscriptionStatus.PastDue);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubscriptionUpdated_with_no_local_subscription_is_a_noop()
    {
        _subs.Setup(s => s.GetByStripeSubscriptionIdAsync("sub_unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainSubscription?)null);

        StubEvent(EventTypes.CustomerSubscriptionUpdated, new Stripe.Subscription { Id = "sub_unknown", Status = "active" });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvoicePaymentSucceeded_clears_past_due_state()
    {
        var orgId = Guid.NewGuid();
        var org = new Organisation { Id = orgId, StripeCustomerId = "cus_ps", Status = OrgStatus.Suspended };
        var sub = new DomainSubscription { OrgId = orgId, Status = SubscriptionStatus.PastDue, PaymentFailed = true };
        _orgs.Setup(o => o.GetByStripeCustomerIdAsync("cus_ps", It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _subs.Setup(s => s.GetByOrgIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(sub);

        StubEvent(EventTypes.InvoicePaymentSucceeded, new Invoice { CustomerId = "cus_ps" });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        sub.PaymentFailed.Should().BeFalse();
        sub.Status.Should().Be(SubscriptionStatus.Active);
        org.Status.Should().Be(OrgStatus.Active);
    }

    [Fact]
    public async Task Duplicate_event_is_skipped_idempotently()
    {
        // The event id is already in the Redis processed set → handler returns early, no writes.
        _db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
        StubEvent(EventTypes.CheckoutSessionCompleted, new Session { Metadata = new Dictionary<string, string>() });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _orgs.Verify(o => o.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Checkout_session_with_missing_metadata_is_ignored()
    {
        StubEvent(EventTypes.CheckoutSessionCompleted, new Session { Metadata = new Dictionary<string, string>() });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        // No org lookup / no write when orgId/planId metadata is absent.
        _orgs.Verify(o => o.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unhandled_event_type_is_marked_processed_without_writes()
    {
        StubEvent("customer.created", new Stripe.Customer { Id = "cus_x" });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _db.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task CreateCheckoutSession_when_not_configured_throws_not_configured()
    {
        var act = async () => await Build(configured: false).CreateCheckoutSessionAsync(
            Guid.NewGuid(), ProPlanId, annual: false, "https://ok", "https://cancel", CancellationToken.None);

        await act.Should().ThrowAsync<BillingException>()
            .Where(e => e.ErrorCode == "BILLING_NOT_CONFIGURED");
    }

    [Fact]
    public async Task CreateCheckoutSession_for_unpurchasable_plan_throws_before_any_network_call()
    {
        var orgId = Guid.NewGuid();
        // Community plan has no Stripe price id (and no env fallback) → PlanNotPurchasable is thrown by
        // ResolvePriceId before the SessionService network call is reached.
        var org = new Organisation { Id = orgId, BillingEmail = "o@e.test" };
        var community = new Flowamaz.Core.Entities.Platform.Plan { Id = PlatformSeedData.CommunityPlanId, Name = "Community", Slug = "community" };
        _orgs.Setup(o => o.GetForUpdateAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _plans.Setup(p => p.GetByIdAsync(PlatformSeedData.CommunityPlanId, It.IsAny<CancellationToken>())).ReturnsAsync(community);

        var act = async () => await Build().CreateCheckoutSessionAsync(
            orgId, PlatformSeedData.CommunityPlanId, annual: false, "https://ok", "https://cancel", CancellationToken.None);

        await act.Should().ThrowAsync<BillingException>()
            .Where(e => e.ErrorCode == "BILLING_PLAN_NOT_PURCHASABLE");
    }

    [Fact]
    public async Task CreateCustomerPortalSession_without_customer_throws_no_customer()
    {
        var orgId = Guid.NewGuid();
        _orgs.Setup(o => o.GetForUpdateAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organisation { Id = orgId, StripeCustomerId = null });

        var act = async () => await Build().CreateCustomerPortalSessionAsync(orgId, "https://return", CancellationToken.None);

        await act.Should().ThrowAsync<BillingException>();
    }

    [Fact]
    public async Task InvoicePaymentFailed_sets_payment_failed_flag()
    {
        var orgId = Guid.NewGuid();
        var org = new Organisation { Id = orgId, StripeCustomerId = "cus_pf", BillingEmail = "owner@example.com", Status = OrgStatus.Active };
        var sub = new DomainSubscription { OrgId = orgId, Status = SubscriptionStatus.Active };
        _orgs.Setup(o => o.GetByStripeCustomerIdAsync("cus_pf", It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _subs.Setup(s => s.GetByOrgIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(sub);

        StubEvent(EventTypes.InvoicePaymentFailed, new Invoice { CustomerId = "cus_pf" });

        await Build().HandleWebhookAsync("{}", "sig", CancellationToken.None);

        sub.PaymentFailed.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.PastDue);
        org.Status.Should().Be(OrgStatus.Suspended);
        _email.Verify(e => e.SendAsync(
            "owner@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
