using Flowamaz.Core.Entities.Auth;

namespace Flowamaz.Core.Interfaces.Repositories;

/// <summary>
/// Persistence for signed-in CLIs (<see cref="CliSession"/>). Lookup by token is by hash only. Every query is
/// scoped to one person (<c>orgUserId</c>) so nobody can see or end another person's CLI sessions.
/// </summary>
public interface ICliSessionRepository
{
    Task AddAsync(CliSession session, CancellationToken cancellationToken = default);

    /// <summary>Tracked, so the caller can update it and save.</summary>
    Task<CliSession?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Tracked. Null when the session doesn't exist or belongs to someone else.</summary>
    Task<CliSession?> GetForUserAsync(Guid orgUserId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Live (not revoked, not expired) sessions for one person, most recently used first.</summary>
    Task<IReadOnlyList<CliSession>> ListActiveForUserAsync(Guid orgUserId, CancellationToken cancellationToken = default);
}
