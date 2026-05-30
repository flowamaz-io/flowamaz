using Flowamaz.Core.Entities.Auth;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>Persistence for per-org SSO configuration (one row per org).</summary>
public interface IOrgSsoConfigRepository
{
    Task<OrgSsoConfig?> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task AddAsync(OrgSsoConfig config, CancellationToken cancellationToken = default);
    void Update(OrgSsoConfig config);
}
