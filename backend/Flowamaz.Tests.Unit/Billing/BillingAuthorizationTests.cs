using FluentAssertions;
using Flowamaz.Api.Authorization;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;

namespace Flowamaz.Tests.Unit.Billing;

/// <summary>
/// The billing checkout/portal/usage endpoints are gated by <see cref="RequireOrgOwnerAttribute"/>
/// → <see cref="OrgOwnerAuthorizationHandler"/>. A non-owner (authenticated org member) must be
/// rejected with HTTP 403 before the action runs (prompt 07-01).
/// </summary>
public class BillingAuthorizationTests
{
    private static AuthorizationFilterContext BuildContext()
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, []);
    }

    [Fact]
    public void Non_owner_is_forbidden_on_billing_endpoint()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUser.SetupGet(u => u.IsOrgOwner).Returns(false);

        var handler = new OrgOwnerAuthorizationHandler(currentUser.Object);
        var ctx = BuildContext();

        handler.OnAuthorization(ctx);

        ctx.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public void Owner_is_allowed_on_billing_endpoint()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUser.SetupGet(u => u.IsOrgOwner).Returns(true);

        var handler = new OrgOwnerAuthorizationHandler(currentUser.Object);
        var ctx = BuildContext();

        handler.OnAuthorization(ctx);

        ctx.Result.Should().BeNull();
    }
}
