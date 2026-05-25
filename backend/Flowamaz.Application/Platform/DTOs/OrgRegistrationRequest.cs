using Flowamaz.Core.Enums;

namespace Flowamaz.Application.Platform.DTOs;

/// <summary>
/// API request shape for organisation self-registration. Validated by
/// <see cref="Validators.OrgRegistrationValidator"/>; the endpoint (prompt 04/05) maps it onto
/// <see cref="Core.Interfaces.Services.IOrganisationService.RegisterOrganisationAsync"/>.
/// </summary>
public sealed class OrgRegistrationRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string BillingEmail { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerPassword { get; set; } = string.Empty;
    public string PlanSlug { get; set; } = "community";
    public DataRegion DataRegion { get; set; } = DataRegion.ApSoutheast1;
}
