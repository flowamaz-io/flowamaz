using Flowamaz.Core.Entities.Auth;

namespace Flowamaz.Core.Interfaces.Repositories;

public interface IPasswordResetTokenRepository
{
    Task AddAsync(PasswordResetToken token, CancellationToken ct = default);
    Task<PasswordResetToken?> GetByHashAsync(string hash, CancellationToken ct = default);

    /// <summary>Marks every unused reset link for the user as used (e.g. when their email changes).</summary>
    Task InvalidateAllForUserAsync(Guid orgUserId, CancellationToken ct = default);
}
