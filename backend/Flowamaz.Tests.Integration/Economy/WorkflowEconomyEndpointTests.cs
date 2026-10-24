using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flowamaz.Core.Entities.Economy;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Economy;

/// <summary>
/// Workflow Economy against the real database: a paid community template hides its YAML, refuses
/// install with 402 until the workspace owns it, and installs once an active purchase exists.
/// (Checkout itself needs Stripe and is covered by unit tests with a fake gateway.)
/// </summary>
[Collection("api")]
public class WorkflowEconomyEndpointTests : ApiTestBase
{
    public WorkflowEconomyEndpointTests(IntegrationApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Paid_template_is_gated_until_purchased()
    {
        var buyer = await RegisterOwnerAsync("econ-buyer");
        var ws = await CreateWorkspaceAsync(buyer.Client, "Buyer", "econ-buyer-ws");

        // Copy an official template's YAML into a paid community listing from another org.
        var list = await Fixture.NewClient().GetAsync("/api/v1/templates?pageSize=50");
        var officialId = (await DataAsync(list)).GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();
        var paid = new WorkflowTemplate
        {
            Name = "Paid AP Flow", Slug = $"paid-ap-{Guid.NewGuid():N}"[..20], Description = "d", Category = "Finance",
            OrgId = Guid.NewGuid(), ReviewStatus = "approved", PricingModel = TemplatePricing.OneTime, PriceCents = 4999, Currency = "usd",
        };
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            paid.YamlContent = db.WorkflowTemplates.Single(t => t.Id == officialId).YamlContent;
            db.WorkflowTemplates.Add(paid);
            await db.SaveChangesAsync();
        }

        var detail = await DataAsync(await Fixture.NewClient().GetAsync($"/api/v1/templates/{paid.Id}"));
        detail.GetProperty("yamlContent").GetString().Should().BeEmpty();
        detail.GetProperty("pricingModel").GetString().Should().Be("one_time");
        detail.GetProperty("priceCents").GetInt32().Should().Be(4999);

        var access = await DataAsync(await buyer.Client.GetAsync($"/api/v1/workspaces/{ws}/templates/{paid.Id}/access"));
        access.GetProperty("requiresPurchase").GetBoolean().Should().BeTrue();
        access.GetProperty("owned").GetBoolean().Should().BeFalse();

        var blocked = await buyer.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/templates/{paid.Id}/install", new { name = "Mine" });
        blocked.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("TEMPLATE_PURCHASE_REQUIRED");

        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.TemplatePurchases.Add(new TemplatePurchase
            {
                WorkspaceId = ws, OrgId = buyer.OrgId, TemplateId = paid.Id, PublisherOrgId = paid.OrgId!.Value,
                PricingModel = TemplatePricing.OneTime, AmountCents = 4999, FeeCents = 1000, Status = TemplatePurchaseStatus.Active,
                PurchasedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var install = await buyer.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/templates/{paid.Id}/install", new { name = "Mine" });
        install.StatusCode.Should().Be(HttpStatusCode.OK, await install.Content.ReadAsStringAsync());

        var purchases = await DataAsync(await buyer.Client.GetAsync($"/api/v1/workspaces/{ws}/purchases"));
        purchases.EnumerateArray().Should().ContainSingle(p => p.GetProperty("templateName").GetString() == "Paid AP Flow");

        // The gallery is shared across the "api" collection: take the paid listing down again.
        using (var scope = Fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowAmazDbContext>();
            db.WorkflowTemplates.Single(t => t.Id == paid.Id).IsActive = false;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Publisher_status_starts_disconnected_and_onboarding_needs_stripe()
    {
        var owner = await RegisterOwnerAsync("econ-pub");

        var status = await DataAsync(await owner.Client.GetAsync("/api/v1/publisher"));
        status.GetProperty("connected").GetBoolean().Should().BeFalse();
        status.GetProperty("platformFeePercent").GetDecimal().Should().Be(20m);

        var onboard = await owner.Client.PostAsJsonAsync("/api/v1/publisher/onboarding",
            new { returnUrl = "http://localhost:5173/settings/publisher", refreshUrl = "http://localhost:5173/settings/publisher" });
        ((int)onboard.StatusCode).Should().BeOneOf(503, 400);
    }
}
