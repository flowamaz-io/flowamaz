namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Ambient identity of the current request. Read by DbContext to stamp CreatedBy/UpdatedBy
/// on AuditableEntity saves, and by authorisation services. Null when unauthenticated
/// (background jobs, migrations, anonymous endpoints).
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? OrgId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
}
