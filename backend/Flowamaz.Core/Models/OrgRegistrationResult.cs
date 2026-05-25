using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Models;

/// <summary>
/// Outcome of a successful organisation registration. Returned by
/// <see cref="Interfaces.Services.IOrganisationService.RegisterOrganisationAsync"/>.
/// Lives in Core because the Core service interface returns it (Core cannot depend on Application).
/// </summary>
public sealed record OrgRegistrationResult(
    Guid OrgId,
    string OrgSlug,
    Guid OwnerUserId,
    string OwnerEmail,
    string PlanSlug,
    OrgStatus Status,
    DateTime TrialEndsAt);
