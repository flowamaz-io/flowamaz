using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;

namespace Flowamaz.Api.Identity;

/// <summary>
/// Reads the request identity that <see cref="Middleware.JwtAuthMiddleware"/> resolved into
/// <c>HttpContext.Items["CurrentUser"]</c>. Lives in the Api layer because it depends on
/// HttpContext (a web concern); Infrastructure stays free of the web framework. Returns an
/// unauthenticated view when no context is present (auth endpoints, background work, migrations).
/// </summary>
public sealed class HttpContextCurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    private CurrentUserContext? Context =>
        accessor.HttpContext?.Items.TryGetValue(CurrentUserContext.HttpContextItemKey, out var value) == true
            ? value as CurrentUserContext
            : null;

    public Guid? UserId => Context?.OrgUserId;
    public Guid? OrgId => Context?.OrgId;
    public string? OrgSlug => Context?.OrgSlug;
    public string? Email => Context?.Email;
    public bool IsAuthenticated => Context is not null;
    public bool IsOrgOwner => Context?.IsOrgOwner ?? false;
    public bool IsHumanUser => Context is { IsApiKey: false };
    public bool IsApiKey => Context is { IsApiKey: true };
    public IReadOnlyList<WorkspaceMembership> WorkspaceMemberships => Context?.WorkspaceMemberships ?? [];
    public Guid? ApiKeyWorkspaceId => Context?.ApiKeyWorkspaceId;
    public Guid? ApiKeyEnvironmentId => Context?.ApiKeyEnvironmentId;
    public IReadOnlyList<string> ApiKeyScopes => Context?.ApiKeyScopes ?? [];

    public WorkspaceRole? GetWorkspaceRole(Guid workspaceId)
    {
        var membership = Context?.WorkspaceMemberships.FirstOrDefault(m => m.WorkspaceId == workspaceId);
        return membership is null ? null : (WorkspaceRole)membership.RoleValue;
    }
}
