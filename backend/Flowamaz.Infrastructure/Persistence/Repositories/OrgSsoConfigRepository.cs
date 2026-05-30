using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flowamaz.Infrastructure.Persistence.Repositories;

/// <summary>Persistence for per-org SSO configuration.</summary>
public sealed class OrgSsoConfigRepository(FlowAmazDbContext db) : IOrgSsoConfigRepository
{
    public Task<OrgSsoConfig?> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default) =>
        db.OrgSsoConfigs.FirstOrDefaultAsync(c => c.OrgId == orgId, cancellationToken);

    public async Task AddAsync(OrgSsoConfig config, CancellationToken cancellationToken = default) =>
        await db.OrgSsoConfigs.AddAsync(config, cancellationToken);

    public void Update(OrgSsoConfig config) => db.OrgSsoConfigs.Update(config);
}
