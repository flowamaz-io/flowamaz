using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Seed;
using Flowamaz.Tests.Integration.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase7;

/// <summary>
/// Billing webhook security over the real server (prompt 07-01/07-07). The Stripe webhook secret is
/// a known fixture value, so these tests compute a VALID Stripe-Signature header
/// (t=&lt;ts&gt;,v1=&lt;HMACSHA256(ts.payload, secret)&gt;) and drive the real
/// EventUtility.ConstructEvent verification path end to end: a signed checkout.session.completed
/// upgrades the org plan; customer.subscription.deleted downgrades to Community; a garbage signature
/// is rejected with 400. No STRIPE_SECRET_KEY is configured, so no Stripe network call is ever made.
/// </summary>
[Collection("api")]
public class Phase7BillingTests : ApiTestBase
{
    public Phase7BillingTests(IntegrationApiFixture fixture) : base(fixture) { }

    /// <summary>Computes a real Stripe-Signature header over the payload using the fixture secret.</summary>
    private static string Sign(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signedPayload = $"{timestamp}.{payload}";
        var key = Encoding.UTF8.GetBytes(IntegrationApiFixture.StripeWebhookSecret);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var signature = Convert.ToHexStringLower(hash);
        return $"t={timestamp},v1={signature}";
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string payload, string signature)
    {
        var client = Fixture.NewClient();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("Stripe-Signature", signature);
        return await client.PostAsync("/api/v1/billing/webhook", content);
    }

    // The SDK version string so EventUtility.ConstructEvent accepts the event (api_version must be
    // present and non-null, else Stripe's compatibility check NREs).
    private static readonly string ApiVersion = Stripe.StripeConfiguration.ApiVersion;

    private static string CheckoutCompletedPayload(Guid orgId, Guid planId, string subscriptionId, string customerId)
    {
        // Minimal-but-valid checkout.session.completed event shape (the fields the handler reads).
        var obj = $$"""
            {
              "id": "evt_{{Guid.NewGuid():N}}",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "checkout.session.completed",
              "data": {
                "object": {
                  "id": "cs_test_{{Guid.NewGuid():N}}",
                  "object": "checkout.session",
                  "customer": "{{customerId}}",
                  "subscription": "{{subscriptionId}}",
                  "mode": "subscription",
                  "metadata": {
                    "orgId": "{{orgId}}",
                    "planId": "{{planId}}",
                    "billingCycle": "Monthly"
                  }
                }
              }
            }
            """;
        return obj;
    }

    private static string SubscriptionDeletedPayload(string subscriptionId, string customerId)
    {
        return $$"""
            {
              "id": "evt_{{Guid.NewGuid():N}}",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "customer.subscription.deleted",
              "data": {
                "object": {
                  "id": "{{subscriptionId}}",
                  "object": "subscription",
                  "customer": "{{customerId}}",
                  "status": "canceled"
                }
              }
            }
            """;
    }

    private async Task<Organisation> ReloadOrgAsync(Guid orgId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
        return await db.Organisations.AsNoTracking().FirstAsync(o => o.Id == orgId);
    }

    [Fact]
    public async Task Webhook_checkout_session_completed_with_valid_signature_upgrades_org_plan()
    {
        var owner = await RegisterOwnerAsync("p7-bill-up");
        var subId = $"sub_{Guid.NewGuid():N}";
        var payload = CheckoutCompletedPayload(owner.OrgId, PlatformSeedData.ProPlanId, subId, $"cus_{Guid.NewGuid():N}");

        var resp = await PostWebhookAsync(payload, Sign(payload));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var org = await ReloadOrgAsync(owner.OrgId);
        org.PlanId.Should().Be(PlatformSeedData.ProPlanId);
    }

    [Fact]
    public async Task Webhook_subscription_deleted_downgrades_org_to_community()
    {
        var owner = await RegisterOwnerAsync("p7-bill-down");
        var subId = $"sub_{Guid.NewGuid():N}";
        var customerId = $"cus_{Guid.NewGuid():N}";

        // First upgrade so a local subscription with this StripeSubscriptionId exists.
        var upgrade = CheckoutCompletedPayload(owner.OrgId, PlatformSeedData.ProPlanId, subId, customerId);
        (await PostWebhookAsync(upgrade, Sign(upgrade))).EnsureSuccessStatusCode();
        (await ReloadOrgAsync(owner.OrgId)).PlanId.Should().Be(PlatformSeedData.ProPlanId);

        // Then cancel — org downgrades back to Community.
        var deleted = SubscriptionDeletedPayload(subId, customerId);
        var resp = await PostWebhookAsync(deleted, Sign(deleted));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        (await ReloadOrgAsync(owner.OrgId)).PlanId.Should().Be(PlatformSeedData.CommunityPlanId);
    }

    [Fact]
    public async Task Webhook_with_invalid_signature_is_rejected_with_400()
    {
        var owner = await RegisterOwnerAsync("p7-bill-bad");
        var payload = CheckoutCompletedPayload(owner.OrgId, PlatformSeedData.ProPlanId, "sub_x", "cus_x");

        var resp = await PostWebhookAsync(payload, "t=123,v1=deadbeefnotavalidsignature");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // Org plan must be unchanged — a forged event never mutates state.
        (await ReloadOrgAsync(owner.OrgId)).PlanId.Should().Be(PlatformSeedData.CommunityPlanId);
    }

    [Fact(Skip = "CreateCheckoutSession calls Stripe's live SessionService API (network). Not run in CI; " +
        "the not-configured (no STRIPE_SECRET_KEY) graceful path is asserted below and the create path is unit-tested.")]
    public Task Checkout_returns_stripe_url() => Task.CompletedTask;

    [Fact]
    public async Task Checkout_without_stripe_configured_returns_actionable_error()
    {
        // No STRIPE_SECRET_KEY in the test host → StripeService throws BillingException.NotConfigured
        // before any network call. Verifies the graceful degradation path (502 with an actionable message).
        var owner = await RegisterOwnerAsync("p7-bill-cfg");
        var resp = await owner.Client.PostAsJsonAsync("/api/v1/billing/checkout", new
        {
            planId = PlatformSeedData.ProPlanId,
            annual = false,
            successUrl = "https://app.flowamaz.io/billing/success",
            cancelUrl = "https://app.flowamaz.io/billing/cancel",
        });

        resp.IsSuccessStatusCode.Should().BeFalse();
        (await MessageAsync(resp)).Should().NotBeNullOrWhiteSpace();
    }
}
