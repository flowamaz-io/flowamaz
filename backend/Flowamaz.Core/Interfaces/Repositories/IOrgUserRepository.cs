using Flowamaz.Core.Entities.Platform;

namespace Flowamaz.Core.Interfaces.Repositories;

public interface IOrgUserRepository
{
    Task<OrgUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrgUser?> GetByEmailAndOrgAsync(string email, Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>Stage a new org user for insertion (persisted on SaveChanges).</summary>
    Task AddAsync(OrgUser orgUser, CancellationToken cancellationToken = default);

    /// <summary>
    /// People in this organisation whose account is linked to an identity at the SSO provider, matching
    /// <paramref name="search"/> (part of the email or name) when given, by email; and how many match in all.
    /// </summary>
    Task<(List<OrgUser> Users, int Total)> GetSsoLinkedAsync(Guid orgId, string? search, int take, CancellationToken cancellationToken = default);
}
