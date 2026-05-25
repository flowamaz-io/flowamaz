using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Repositories;

public interface IOrgUserRepository
{
    Task<OrgUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrgUser?> GetByEmailAndOrgAsync(string email, Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>Stage a new org user for insertion (persisted on SaveChanges).</summary>
    Task AddAsync(OrgUser orgUser, CancellationToken cancellationToken = default);
}
