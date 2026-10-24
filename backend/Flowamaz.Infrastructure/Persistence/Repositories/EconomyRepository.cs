using Flowamaz.Core.Entities.Economy;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Workflow Economy persistence. Purchase reads from API paths always filter by workspace_id.</summary>
public sealed class EconomyRepository(FlowAmazDbContext db) : IEconomyRepository
{
    public Task<PublisherAccount?> GetPublisherByOrgAsync(Guid orgId, CancellationToken ct = default) =>
        db.PublisherAccounts.FirstOrDefaultAsync(p => p.OrgId == orgId, ct);

    public Task<PublisherAccount?> GetPublisherByStripeAccountAsync(string accountId, CancellationToken ct = default) =>
        db.PublisherAccounts.FirstOrDefaultAsync(p => p.StripeAccountId == accountId, ct);

    public async Task AddPublisherAsync(PublisherAccount account, CancellationToken ct = default) =>
        await db.PublisherAccounts.AddAsync(account, ct);

    public Task<TemplatePurchase?> GetPurchaseAsync(Guid workspaceId, Guid purchaseId, CancellationToken ct = default) =>
        db.TemplatePurchases.FirstOrDefaultAsync(p => p.WorkspaceId == workspaceId && p.Id == purchaseId, ct);

    public Task<TemplatePurchase?> GetPurchaseForWebhookAsync(Guid purchaseId, CancellationToken ct = default) =>
        db.TemplatePurchases.FirstOrDefaultAsync(p => p.Id == purchaseId, ct);

    public Task<TemplatePurchase?> GetPurchaseBySubscriptionAsync(string subscriptionId, CancellationToken ct = default) =>
        db.TemplatePurchases.FirstOrDefaultAsync(p => p.StripeSubscriptionId == subscriptionId, ct);

    public Task<TemplatePurchase?> GetPurchaseByPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default) =>
        db.TemplatePurchases.FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId, ct);

    public Task<List<TemplatePurchase>> ListPurchasesAsync(Guid workspaceId, Guid? templateId = null, CancellationToken ct = default) =>
        db.TemplatePurchases.AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && (templateId == null || p.TemplateId == templateId))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task AddPurchaseAsync(TemplatePurchase purchase, CancellationToken ct = default) =>
        await db.TemplatePurchases.AddAsync(purchase, ct);

    public Task<bool> EarningExistsAsync(string stripeObjectId, string type, CancellationToken ct = default) =>
        db.PublisherEarnings.AnyAsync(e => e.StripeObjectId == stripeObjectId && e.Type == type, ct);

    public async Task AddEarningAsync(PublisherEarning earning, CancellationToken ct = default) =>
        await db.PublisherEarnings.AddAsync(earning, ct);

    public Task<List<PublisherEarning>> ListEarningsAsync(Guid publisherOrgId, int take, CancellationToken ct = default) =>
        db.PublisherEarnings.AsNoTracking()
            .Where(e => e.PublisherOrgId == publisherOrgId)
            .OrderByDescending(e => e.OccurredAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<(long Gross, long Fee, long Net, int Sales)> SumEarningsAsync(Guid publisherOrgId, CancellationToken ct = default)
    {
        var rows = db.PublisherEarnings.AsNoTracking().Where(e => e.PublisherOrgId == publisherOrgId);
        var gross = await rows.SumAsync(e => (long)e.GrossCents, ct);
        var fee = await rows.SumAsync(e => (long)e.FeeCents, ct);
        var net = await rows.SumAsync(e => (long)e.NetCents, ct);
        var sales = await rows.CountAsync(e => e.Type == "sale", ct);
        return (gross, fee, net, sales);
    }

    public Task<int> CountActiveSubscriptionsAsync(Guid publisherOrgId, CancellationToken ct = default) =>
        db.TemplatePurchases.CountAsync(p => p.PublisherOrgId == publisherOrgId && p.PricingModel == "monthly"
            && (p.Status == TemplatePurchaseStatus.Active || p.Status == TemplatePurchaseStatus.PastDue), ct);

    public async Task<Dictionary<Guid, (int Sales, int Net)>> EarningsByTemplateAsync(Guid publisherOrgId, CancellationToken ct = default)
    {
        var rows = await db.PublisherEarnings.AsNoTracking()
            .Where(e => e.PublisherOrgId == publisherOrgId)
            .GroupBy(e => e.TemplateId)
            .Select(g => new { g.Key, Sales = g.Count(e => e.Type == "sale"), Net = g.Sum(e => e.NetCents) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => (r.Sales, r.Net));
    }

    public Task<List<WorkflowTemplate>> ListTemplatesByOrgAsync(Guid orgId, CancellationToken ct = default) =>
        db.WorkflowTemplates.AsNoTracking()
            .Where(t => t.OrgId == orgId && t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public Task<WorkflowTemplate?> GetOwnedTemplateAsync(Guid orgId, Guid templateId, CancellationToken ct = default) =>
        db.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.OrgId == orgId && t.IsActive, ct);

    public async Task<Dictionary<Guid, string>> TemplateNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.WorkflowTemplates.AsNoTracking().IgnoreQueryFilters()
            .Where(t => list.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }
}
