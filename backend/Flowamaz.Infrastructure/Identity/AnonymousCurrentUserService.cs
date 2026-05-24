using Flowamaz.Core.Interfaces.Services;

namespace Flowamaz.Infrastructure.Identity;

/// <summary>
/// Stand-in <see cref="ICurrentUserService"/> for the bootstrap phase (no auth controllers yet).
/// Returns null identity so DbContext audit stamping does not crash. Real implementation
/// reading <c>HttpContext.User</c> ships in prompt 04.
/// </summary>
public sealed class AnonymousCurrentUserService : ICurrentUserService
{
    public Guid? UserId => null;
    public Guid? OrgId => null;
    public string? Email => null;
    public bool IsAuthenticated => false;
}
