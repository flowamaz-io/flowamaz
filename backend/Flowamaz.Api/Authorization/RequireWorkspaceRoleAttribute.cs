using Flowamaz.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Authorization;

/// <summary>
/// Gates an action on a minimum workspace role (FUNCTIONAL.md §4). Resolves to
/// <see cref="WorkspaceAuthorizationHandler"/> via DI, passing the required role.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireWorkspaceRoleAttribute : TypeFilterAttribute
{
    public RequireWorkspaceRoleAttribute(WorkspaceRole minimumRole) : base(typeof(WorkspaceAuthorizationHandler))
    {
        Arguments = [minimumRole];
    }
}
