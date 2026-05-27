using Flowamaz.Core.Entities.Auth;

namespace Flowamaz.Core.Interfaces.Repositories;

public interface IPasswordResetTokenRepository
{
    Task AddAsync(PasswordResetToken token, CancellationToken ct = default);
    Task<PasswordResetToken?> GetByHashAsync(string hash, CancellationToken ct = default);
}
