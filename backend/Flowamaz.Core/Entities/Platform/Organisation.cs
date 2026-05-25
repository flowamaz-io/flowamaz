using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Platform;

/// <summary>
/// A customer company — tier 2 of the four-tier hierarchy (FUNCTIONAL.md §2.1).
/// Soft-deletable (inherits <see cref="BaseEntity"/>). <see cref="Slug"/> is globally unique.
/// References its <see cref="PlanId"/> and billing region by scalar key, matching the
/// scaffold's convention of scalar keys over EF navigations.
/// </summary>
public class Organisation : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string BillingEmail { get; set; } = string.Empty;
    public string? StripeCustomerId { get; set; }
    public Guid PlanId { get; set; }
    public OrgStatus Status { get; set; } = OrgStatus.Trial;
    public DateTime TrialEndsAt { get; set; }
    public DataRegion DataRegion { get; set; } = DataRegion.ApSoutheast1;
}
