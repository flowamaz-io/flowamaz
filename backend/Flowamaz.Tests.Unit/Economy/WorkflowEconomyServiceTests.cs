using FluentAssertions;
using Flowamaz.Application.Economy;
using Flowamaz.Core.Entities.Economy;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Economy;

[Trait("Category", "Economy")]
public sealed class WorkflowEconomyServiceTests
{
    private readonly Mock<IEconomyRepository> _repo = new();
    private readonly Mock<IWorkflowTemplateRepository> _templates = new();
    private readonly Mock<IOrganisationRepository> _orgs = new();
    private readonly Mock<IStripeConnectGateway> _stripe = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<PublisherEarning> _earnings = [];
    private readonly List<TemplatePurchase> _purchases = [];

    private readonly Guid _buyerOrg = Guid.NewGuid();
    private readonly Guid _sellerOrg = Guid.NewGuid();
    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public WorkflowEconomyServiceTests()
    {
        _stripe.SetupGet(s => s.IsConfigured).Returns(true);
        _repo.Setup(r => r.AddEarningAsync(It.IsAny<PublisherEarning>(), It.IsAny<CancellationToken>()))
            .Callback<PublisherEarning, CancellationToken>((e, _) => _earnings.Add(e)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.EarningExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string type, CancellationToken _) => _earnings.Any(e => e.StripeObjectId == id && e.Type == type));
        _repo.Setup(r => r.AddPurchaseAsync(It.IsAny<TemplatePurchase>(), It.IsAny<CancellationToken>()))
            .Callback<TemplatePurchase, CancellationToken>((p, _) => _purchases.Add(p)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ListPurchasesAsync(_ws, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? t, CancellationToken _) => _purchases.Where(p => t == null || p.TemplateId == t).ToList());
        _orgs.Setup(o => o.GetByIdAsync(_buyerOrg, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organisation { Id = _buyerOrg, Name = "Buyer", BillingEmail = "owner@buyer.test" });
    }

    private WorkflowEconomyService Build(decimal? fee = null)
    {
        var settings = new Dictionary<string, string?>();
        if (fee is not null) settings["Economy:PlatformFeePercent"] = fee.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new WorkflowEconomyService(_repo.Object, _templates.Object, _orgs.Object, _stripe.Object, _uow.Object,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger<WorkflowEconomyService>.Instance);
    }

    private WorkflowTemplate PaidTemplate(string model = TemplatePricing.OneTime, int price = 4999) => new()
    {
        Id = Guid.NewGuid(), Name = "AP Automation", OrgId = _sellerOrg, ReviewStatus = "approved",
        PricingModel = model, PriceCents = price, Currency = "usd",
    };

    private void SellerReady(bool ready = true) =>
        _repo.Setup(r => r.GetPublisherByOrgAsync(_sellerOrg, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublisherAccount { OrgId = _sellerOrg, StripeAccountId = "acct_seller", ChargesEnabled = ready, StatusRefreshedAt = DateTime.UtcNow });

    [Theory]
    [InlineData(4999, 1000)]
    [InlineData(100, 20)]
    [InlineData(2500, 500)]
    [InlineData(1234, 247)]
    public void Fee_IsTwentyPercentRoundedHalfUp(int amount, int expected) =>
        TemplatePricing.Fee(amount, 20m).Should().Be(expected);

    [Fact]
    public void PlatformFee_DefaultsToTwentyPercent() => Build().PlatformFeePercent.Should().Be(20m);

    [Theory]
    [InlineData("one_time", 4999, "btc", "INVALID_TEMPLATE_PRICE")]
    [InlineData("one_time", 50, "usd", "INVALID_TEMPLATE_PRICE")]
    [InlineData("monthly", 2_000_000, "usd", "INVALID_TEMPLATE_PRICE")]
    public async Task ValidatePricing_RejectsBadPrices(string model, int price, string currency, string code)
    {
        SellerReady();
        var act = () => Build().ValidatePricingAsync(_sellerOrg, new TemplatePricingRequest(model, price, currency));
        (await act.Should().ThrowAsync<EconomyException>()).Which.ErrorCode.Should().Be(code);
    }

    [Fact]
    public async Task ValidatePricing_PaidNeedsAReadyPublisher()
    {
        SellerReady(false);
        var act = () => Build().ValidatePricingAsync(_sellerOrg, new TemplatePricingRequest("monthly", 1900, "usd"));
        (await act.Should().ThrowAsync<EconomyException>()).Which.ErrorCode.Should().Be("PUBLISHER_NOT_READY");
    }

    [Fact]
    public async Task ValidatePricing_FreeNeedsNothing() =>
        await Build().Invoking(s => s.ValidatePricingAsync(_sellerOrg, new TemplatePricingRequest("free", 0, "usd")))
            .Should().NotThrowAsync();

    [Fact]
    public async Task StartPurchase_CreatesPendingPurchase_AndDestinationCheckoutWithFee()
    {
        var template = PaidTemplate();
        _templates.Setup(t => t.GetByIdAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        SellerReady();
        TemplateCheckoutRequest? sent = null;
        _stripe.Setup(s => s.CreateTemplateCheckoutAsync(It.IsAny<TemplateCheckoutRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TemplateCheckoutRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new TemplateCheckoutSession("cs_test_1", "https://checkout.stripe.test/cs_test_1"));

        var url = await Build().StartPurchaseAsync(_ws, _buyerOrg, _user, template.Id, "https://app/ok", "https://app/no");

        url.Should().Be("https://checkout.stripe.test/cs_test_1");
        var purchase = _purchases.Should().ContainSingle().Subject;
        purchase.Status.Should().Be(TemplatePurchaseStatus.Pending);
        purchase.FeeCents.Should().Be(1000);
        purchase.StripeCheckoutSessionId.Should().Be("cs_test_1");
        sent!.DestinationAccountId.Should().Be("acct_seller");
        sent.FeeCents.Should().Be(1000);
        sent.Monthly.Should().BeFalse();
        sent.CustomerEmail.Should().Be("owner@buyer.test");
    }

    [Fact]
    public async Task StartPurchase_RefusesFreeUnapprovedOwnedOrUnavailableSeller()
    {
        var svc = Build();
        var free = PaidTemplate(TemplatePricing.Free, 0);
        var pending = PaidTemplate(); pending.ReviewStatus = "pending";
        var owned = PaidTemplate(); owned.OrgId = _buyerOrg;
        var noSeller = PaidTemplate();
        foreach (var t in new[] { free, pending, owned, noSeller })
            _templates.Setup(r => r.GetByIdAsync(t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t);
        SellerReady(false);

        async Task<string> Code(WorkflowTemplate t) =>
            (await FluentActions.Awaiting(() => svc.StartPurchaseAsync(_ws, _buyerOrg, _user, t.Id, "u", "u"))
                .Should().ThrowAsync<EconomyException>()).Which.ErrorCode;

        (await Code(free)).Should().Be("TEMPLATE_NOT_FOR_SALE");
        (await Code(pending)).Should().Be("TEMPLATE_NOT_APPROVED");
        (await Code(owned)).Should().Be("TEMPLATE_ALREADY_OWNED");
        (await Code(noSeller)).Should().Be("TEMPLATE_SELLER_UNAVAILABLE");
    }

    [Fact]
    public async Task Entitlement_FreeAndPublisherOrgAlwaysAllowed_PurchaseActiveRequiredOtherwise()
    {
        var svc = Build();
        var template = PaidTemplate();

        (await svc.HasEntitlementAsync(_ws, _sellerOrg, template)).Should().BeTrue("publishers use their own workflows");
        (await svc.HasEntitlementAsync(_ws, _buyerOrg, PaidTemplate(TemplatePricing.Free, 0))).Should().BeTrue();
        (await svc.HasEntitlementAsync(_ws, _buyerOrg, template)).Should().BeFalse();

        _purchases.Add(new TemplatePurchase { WorkspaceId = _ws, TemplateId = template.Id, Status = TemplatePurchaseStatus.Canceled });
        (await svc.HasEntitlementAsync(_ws, _buyerOrg, template)).Should().BeFalse();

        _purchases.Add(new TemplatePurchase { WorkspaceId = _ws, TemplateId = template.Id, Status = TemplatePurchaseStatus.Active });
        (await svc.HasEntitlementAsync(_ws, _buyerOrg, template)).Should().BeTrue();
        (await svc.GetAccessAsync(_ws, _buyerOrg, template)).Should().Match<TemplateAccess>(a => a.RequiresPurchase && a.Owned);
    }

    private TemplatePurchase PendingPurchase(string model = TemplatePricing.OneTime)
    {
        var p = new TemplatePurchase
        {
            WorkspaceId = _ws, OrgId = _buyerOrg, TemplateId = Guid.NewGuid(), PublisherOrgId = _sellerOrg,
            PricingModel = model, AmountCents = 4999, FeeCents = 1000, Currency = "usd",
        };
        _repo.Setup(r => r.GetPurchaseForWebhookAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        return p;
    }

    private static EconomyCheckoutCompleted Completed(TemplatePurchase p, string status = "paid", string? sub = null) => new(
        "cs_test_9", new Dictionary<string, string> { ["kind"] = "template_purchase", ["purchaseId"] = p.Id.ToString() },
        status, sub is null ? "pi_test_9" : null, sub, 4999, "usd");

    [Fact]
    public async Task CheckoutCompleted_ActivatesPurchase_AndRecordsOneSale()
    {
        var p = PendingPurchase();
        var svc = Build();

        (await svc.HandleCheckoutCompletedAsync(Completed(p))).Should().BeTrue();
        (await svc.HandleCheckoutCompletedAsync(Completed(p))).Should().BeTrue(); // webhook replay

        p.Status.Should().Be(TemplatePurchaseStatus.Active);
        p.StripePaymentIntentId.Should().Be("pi_test_9");
        var sale = _earnings.Should().ContainSingle().Subject;
        sale.Should().Match<PublisherEarning>(e => e.Type == "sale" && e.GrossCents == 4999 && e.FeeCents == 1000 && e.NetCents == 3999
            && e.PublisherOrgId == _sellerOrg);
    }

    [Fact]
    public async Task CheckoutCompleted_Unpaid_StaysPending()
    {
        var p = PendingPurchase();
        await Build().HandleCheckoutCompletedAsync(Completed(p, status: "unpaid"));
        p.Status.Should().Be(TemplatePurchaseStatus.Pending);
        _earnings.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckoutCompleted_ForPlanBilling_IsNotClaimed()
    {
        var handled = await Build().HandleCheckoutCompletedAsync(new EconomyCheckoutCompleted(
            "cs_plan", new Dictionary<string, string> { ["orgId"] = Guid.NewGuid().ToString() }, "paid", null, "sub_plan", 4900, "usd"));
        handled.Should().BeFalse();
    }

    [Fact]
    public async Task Subscription_RenewalsAddLedgerRows_FirstInvoiceDoesNot()
    {
        var p = PendingPurchase(TemplatePricing.Monthly);
        p.StripeSubscriptionId = "sub_tpl";
        _repo.Setup(r => r.GetPurchaseBySubscriptionAsync("sub_tpl", It.IsAny<CancellationToken>())).ReturnsAsync(p);
        var svc = Build();

        await svc.HandleCheckoutCompletedAsync(Completed(p, sub: "sub_tpl"));
        await svc.HandleInvoicePaidAsync(new EconomyInvoicePaid("in_1", "sub_tpl", "subscription_create", 4999, "usd", null));
        await svc.HandleInvoicePaidAsync(new EconomyInvoicePaid("in_2", "sub_tpl", "subscription_cycle", 4999, "usd", null));
        await svc.HandleInvoicePaidAsync(new EconomyInvoicePaid("in_2", "sub_tpl", "subscription_cycle", 4999, "usd", null));

        _earnings.Select(e => e.Type).Should().Equal("sale", "renewal");
        _earnings[1].FeeCents.Should().Be(1000);
        p.CurrentPeriodEnd.Should().BeAfter(DateTime.UtcNow.AddDays(40));
    }

    [Fact]
    public async Task InvoiceForPlanSubscription_IsNotClaimed() =>
        (await Build().HandleInvoicePaidAsync(new EconomyInvoicePaid("in_x", "sub_plan", "subscription_cycle", 4900, "usd", null)))
            .Should().BeFalse();

    [Fact]
    public async Task SubscriptionDeleted_CancelsAccess_PastDueKeepsIt()
    {
        var p = PendingPurchase(TemplatePricing.Monthly);
        p.Status = TemplatePurchaseStatus.Active;
        p.StripeSubscriptionId = "sub_tpl";
        _repo.Setup(r => r.GetPurchaseBySubscriptionAsync("sub_tpl", It.IsAny<CancellationToken>())).ReturnsAsync(p);
        var svc = Build();

        await svc.HandleInvoicePaymentFailedAsync("sub_tpl");
        p.Status.Should().Be(TemplatePurchaseStatus.PastDue);

        await svc.HandleSubscriptionChangedAsync(new EconomySubscriptionChanged("sub_tpl", "canceled", false, null, Deleted: true));
        p.Status.Should().Be(TemplatePurchaseStatus.Canceled);
    }

    [Fact]
    public async Task Refunds_ReverseProportionally_AndFullRefundRevokes()
    {
        var p = PendingPurchase();
        p.Status = TemplatePurchaseStatus.Active;
        p.StripePaymentIntentId = "pi_r";
        _repo.Setup(r => r.GetPurchaseByPaymentIntentAsync("pi_r", It.IsAny<CancellationToken>())).ReturnsAsync(p);
        var svc = Build();

        await svc.HandleChargeRefundedAsync(new EconomyChargeRefunded("ch_1", "pi_r", 4999, 2000, [new EconomyRefund("re_1", 2000)]));
        p.Status.Should().Be(TemplatePurchaseStatus.Active);
        _earnings.Single().Should().Match<PublisherEarning>(e => e.Type == "refund" && e.GrossCents == -2000 && e.FeeCents == -400 && e.NetCents == -1600);

        await svc.HandleChargeRefundedAsync(new EconomyChargeRefunded("ch_1", "pi_r", 4999, 4999,
            [new EconomyRefund("re_1", 2000), new EconomyRefund("re_2", 2999)]));
        p.Status.Should().Be(TemplatePurchaseStatus.Refunded);
        _earnings.Should().HaveCount(2, "re_1 is not recorded twice");
    }

    [Fact]
    public async Task AccountUpdated_RefreshesPublisherFlags()
    {
        var account = new PublisherAccount { OrgId = _sellerOrg, StripeAccountId = "acct_x" };
        _repo.Setup(r => r.GetPublisherByStripeAccountAsync("acct_x", It.IsAny<CancellationToken>())).ReturnsAsync(account);

        await Build().HandleAccountUpdatedAsync(new ConnectAccountStatus("acct_x", true, true, true, "MY", ["external_account"]));

        account.ChargesEnabled.Should().BeTrue();
        account.Country.Should().Be("MY");
        account.RequirementsDue.Should().Equal("external_account");
    }

    [Fact]
    public async Task Onboarding_CreatesExpressAccountOnce()
    {
        _orgs.Setup(o => o.GetByIdAsync(_sellerOrg, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organisation { Id = _sellerOrg, BillingEmail = "seller@test" });
        PublisherAccount? added = null;
        _repo.Setup(r => r.AddPublisherAsync(It.IsAny<PublisherAccount>(), It.IsAny<CancellationToken>()))
            .Callback<PublisherAccount, CancellationToken>((a, _) => added = a).Returns(Task.CompletedTask);
        _stripe.Setup(s => s.CreateExpressAccountAsync(_sellerOrg, "seller@test", It.IsAny<CancellationToken>())).ReturnsAsync("acct_new");
        _stripe.Setup(s => s.CreateOnboardingLinkAsync("acct_new", "r", "f", It.IsAny<CancellationToken>())).ReturnsAsync("https://connect.stripe.test/x");

        var url = await Build().StartOnboardingAsync(_sellerOrg, "r", "f");

        url.Should().Be("https://connect.stripe.test/x");
        added!.StripeAccountId.Should().Be("acct_new");
    }

    [Fact]
    public async Task NotConfigured_FailsWithActionableError()
    {
        _stripe.SetupGet(s => s.IsConfigured).Returns(false);
        (await FluentActions.Awaiting(() => Build().StartOnboardingAsync(_sellerOrg, "r", "f"))
            .Should().ThrowAsync<EconomyException>()).Which.Message.Should().Contain("STRIPE_SECRET_KEY");
    }
}
