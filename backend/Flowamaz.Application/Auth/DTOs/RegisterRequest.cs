namespace Flowamaz.Application.Auth.DTOs;

/// <summary>
/// New-organisation signup. Creates the org, its owner user, and a trial subscription, then
/// returns an authenticated session. <see cref="DataRegion"/> is an optional region code
/// (e.g. "ap-southeast-1"); it defaults to ap-southeast-1.
/// </summary>
public sealed record RegisterRequest(
    string OrgName,
    string OrgSlug,
    string BillingEmail,
    string Email,
    string Name,
    string Password,
    string PlanSlug,
    string? DataRegion = null);
