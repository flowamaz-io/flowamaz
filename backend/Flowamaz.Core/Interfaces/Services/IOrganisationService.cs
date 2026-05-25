using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Organisation lifecycle. <see cref="RegisterOrganisationAsync"/> atomically provisions an
/// org, its owner user, and a 14-day trial subscription, then sends a welcome email.
/// </summary>
public interface IOrganisationService
{
    Task<OrgRegistrationResult> RegisterOrganisationAsync(
        string name,
        string slug,
        string billingEmail,
        string ownerEmail,
        string ownerName,
        string ownerPassword,
        string planSlug,
        DataRegion dataRegion,
        CancellationToken cancellationToken = default);

    Task<Organisation?> GetByIdAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task<Organisation?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Resolve the plan limits in force for an organisation.</summary>
    Task<PlanLimits> GetPlanLimitsAsync(Guid orgId, CancellationToken cancellationToken = default);
}
