using Flowamaz.Application.Platform;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Flowamaz.Api.Authorization;

/// <summary>
/// Gates an action on the caller being Flowamaz staff: the owner, signed in (not with an API key), of an organisation
/// listed in <c>Platform:StaffOrgIds</c> (see <see cref="PlatformStaff"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequirePlatformStaffAttribute : TypeFilterAttribute
{
    public RequirePlatformStaffAttribute() : base(typeof(PlatformStaffAuthorizationHandler)) { }
}

public sealed class PlatformStaffAuthorizationHandler(ICurrentUserService currentUser, IConfiguration configuration) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!currentUser.IsAuthenticated)
        {
            context.Result = Envelope(context, 401, "AUTH_REQUIRED", "Authentication is required. Sign in and try again.");
            return;
        }

        if (currentUser.IsApiKey || !PlatformStaff.IsStaff(configuration, currentUser.OrgId ?? Guid.Empty, currentUser.IsOrgOwner))
        {
            context.Result = Envelope(context, 403, "PLATFORM_STAFF_REQUIRED",
                "Only Flowamaz staff can do this. To change your organisation's API limit, email team@flowamaz.io.");
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
