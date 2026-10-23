using Flowamaz.Core.Entities.Economy;
using Flowamaz.Core.Entities.Library;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Persistence for publisher accounts, template purchases (workspace-scoped) and the earnings ledger.</summary>
public interface IEconomyRepository
{
    Task<PublisherAccount?> GetPublisherByOrgAsync(Guid orgId, CancellationToken ct = default);
    Task<PublisherAccount?> GetPublisherByStripeAccountAsync(string accountId, CancellationToken ct = default);
    Task AddPublisherAsync(PublisherAccount account, CancellationToken ct = default);

    Task<TemplatePurchase?> GetPurchaseAsync(Guid workspaceId, Guid purchaseId, CancellationToken ct = default);
    /// <summary>Webhook lookup by our own id (from Stripe metadata) — not workspace-filtered by design; the id is unguessable and server-issued.</summary>
    Task<TemplatePurchase?> GetPurchaseForWebhookAsync(Guid purchaseId, CancellationToken ct = default);
    Task<TemplatePurchase?> GetPurchaseBySubscriptionAsync(string subscriptionId, CancellationToken ct = default);
    Task<TemplatePurchase?> GetPurchaseByPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default);
    Task<List<TemplatePurchase>> ListPurchasesAsync(Guid workspaceId, Guid? templateId = null, CancellationToken ct = default);
    Task AddPurchaseAsync(TemplatePurchase purchase, CancellationToken ct = default);

    Task<bool> EarningExistsAsync(string stripeObjectId, string type, CancellationToken ct = default);
    Task AddEarningAsync(PublisherEarning earning, CancellationToken ct = default);
    Task<List<PublisherEarning>> ListEarningsAsync(Guid publisherOrgId, int take, CancellationToken ct = default);
    Task<(long Gross, long Fee, long Net, int Sales)> SumEarningsAsync(Guid publisherOrgId, CancellationToken ct = default);
    Task<int> CountActiveSubscriptionsAsync(Guid publisherOrgId, CancellationToken ct = default);
    Task<Dictionary<Guid, (int Sales, int Net)>> EarningsByTemplateAsync(Guid publisherOrgId, CancellationToken ct = default);

    /// <summary>Templates published by an org (any review state), for the listings table.</summary>
    Task<List<WorkflowTemplate>> ListTemplatesByOrgAsync(Guid orgId, CancellationToken ct = default);
    /// <summary>A template owned by the org, tracked for update (any review state).</summary>
    Task<WorkflowTemplate?> GetOwnedTemplateAsync(Guid orgId, Guid templateId, CancellationToken ct = default);
    Task<Dictionary<Guid, string>> TemplateNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
