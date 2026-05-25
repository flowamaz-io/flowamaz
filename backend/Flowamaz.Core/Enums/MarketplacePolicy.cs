namespace Flowamaz.Core.Enums;

/// <summary>
/// How a workspace may install marketplace components (FUNCTIONAL.md §9.2).
/// Stored as a string inside the workspace settings jsonb blob.
/// </summary>
public enum MarketplacePolicy
{
    AllowAll,
    OfficialAndVerified,
    Allowlist,
}
