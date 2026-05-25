using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Flowamaz.Api.Authorization;

/// <summary>
/// Gates an action on the org-owner claim (FUNCTIONAL.md §4.2). Resolves to
/// <see cref="OrgOwnerAuthorizationHandler"/> via DI.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireOrgOwnerAttribute : TypeFilterAttribute
{
    public RequireOrgOwnerAttribute() : base(typeof(OrgOwnerAuthorizationHandler)) { }
}

/// <summary>Allows the request only when the JWT carries the is_org_owner claim.</summary>
public sealed class OrgOwnerAuthorizationHandler(ICurrentUserService currentUser) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!currentUser.IsAuthenticated)
        {
            context.Result = Envelope(context, 401, "AUTH_REQUIRED", "Authentication is required. Sign in and try again.");
            return;
        }

        if (!currentUser.IsOrgOwner)
        {
            context.Result = Envelope(context, 403, "ORG_OWNER_REQUIRED",
                "Only the organisation owner can do this. Ask your org owner to perform this action.");
        }
    }

    private static ObjectResult Envelope(AuthorizationFilterContext context, int status, string code, string message) =>
        new(new
        {
            success = false,
            statusCode = status,
            code,
            message,
            correlationId = context.HttpContext.Items.TryGetValue("CorrelationId", out var v) && v is string s ? s : "",
        })
        { StatusCode = status };
}
