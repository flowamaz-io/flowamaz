namespace Flowamaz.Core.Entities.Economy;

/// <summary>
/// Append-only earnings ledger for publishers: one row per sale, renewal or refund. Idempotent on
/// <see cref="StripeObjectId"/> (the session, invoice or refund id) so webhook replays never
/// double-count. Amounts in minor units; refunds are negative.
/// </summary>
public class PublisherEarning : BaseEntity
{
    public Guid PublisherOrgId { get; set; }
    public Guid PurchaseId { get; set; }
    public Guid TemplateId { get; set; }
    /// <summary>sale | renewal | refund.</summary>
    public string Type { get; set; } = "sale";
    public int GrossCents { get; set; }
    public int FeeCents { get; set; }
    public int NetCents { get; set; }
    public string Currency { get; set; } = "usd";
    public string StripeObjectId { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
