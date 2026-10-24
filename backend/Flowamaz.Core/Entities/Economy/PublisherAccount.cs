namespace Flowamaz.Core.Entities.Economy;

/// <summary>
/// An organisation's seller account for the Workflow Economy (FUNCTIONAL.md §8.9): a Stripe Connect
/// Express account that receives 80% of each sale. One per org. Status flags mirror Stripe and are
/// refreshed from <c>account.updated</c> webhooks and on read. The Stripe account id never leaves the
/// server.
/// </summary>
public class PublisherAccount : AuditableEntity
{
    public Guid OrgId { get; set; }
    public string StripeAccountId { get; set; } = string.Empty;
    public bool ChargesEnabled { get; set; }
    public bool PayoutsEnabled { get; set; }
    public bool DetailsSubmitted { get; set; }
    public string? Country { get; set; }
    /// <summary>Stripe "currently_due" requirement keys, e.g. "external_account".</summary>
    public string[] RequirementsDue { get; set; } = [];
    public DateTime? StatusRefreshedAt { get; set; }
}
