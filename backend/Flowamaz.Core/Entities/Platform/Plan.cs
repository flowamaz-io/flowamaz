using Flowamaz.Core.Models;

namespace Flowamaz.Core.Entities.Platform;

/// <summary>
/// A subscription plan (Community / Starter / Pro / Enterprise). Platform-level reference data,
/// seeded with fixed Guids (FUNCTIONAL.md §13.2). Not a <see cref="BaseEntity"/>: plans are never
/// soft-deleted — they are retired via <see cref="IsActive"/>/<see cref="IsPublic"/>.
/// </summary>
public class Plan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public decimal PriceMonthlyUsd { get; set; }
    public decimal PriceAnnualUsd { get; set; }

    /// <summary>Quantitative caps — stored as jsonb.</summary>
    public PlanLimits Limits { get; set; } = new();

    /// <summary>Boolean feature gates — stored as jsonb.</summary>
    public PlanFeatures Features { get; set; } = new();

    public bool IsActive { get; set; } = true;
    public bool IsPublic { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
