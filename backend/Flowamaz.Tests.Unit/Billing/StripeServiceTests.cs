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
