using System.Security.Claims;
using FluentAssertions;
using Flowamaz.Api.Controllers;
using Flowamaz.Core.Entities.Auth;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Flowamaz.Tests.Unit.Cli;

/// <summary>
/// CliAuthController: the device flow (initiate, poll, approve) and the CLI session endpoints (refresh, logout, list,
/// revoke). Approval binds the approving person — never a copy of the browser's token — and the poll creates a CLI
/// session of its own.
/// </summary>
public class CliAuthControllerTests
{
    private readonly Mock<ICliAuthService> _cliAuth = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly CliSessionServiceTests _svc = new();

    private CliAuthController BuildController(params Claim[] claims)
    {
        var controller = new CliAuthController(_cliAuth.Object, _svc.Build(), _currentUser.Object, _svc.Revocation.Object);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length > 0 ? "test" : null)),
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private void SignedInPerson()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.IsHumanUser).Returns(true);
        _currentUser.Setup(u => u.UserId).Returns(CliSessionServiceTests.UserId);
    }

    [Fact]
    public async Task Device_returns_authorization_with_codes_and_passes_the_client_name()
    {
        _cliAuth.Setup(s => s.InitiateDeviceFlowAsync("laptop", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliDeviceAuthorization("dev123", "ABCD-EFGH", "https://app/cli-auth", "https://app/cli-auth?code=ABCD-EFGH", 2, 600));

        var result = await BuildController().Device(new CliDeviceRequest("laptop"), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<CliDeviceAuthorization>().Which.UserCode.Should().Be("ABCD-EFGH");
    }

    [Fact]
    public async Task Device_without_a_body_still_works_for_older_clis()
    {
        _cliAuth.Setup(s => s.InitiateDeviceFlowAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliDeviceAuthorization("dev123", "ABCD-EFGH", "u", "u?code=ABCD-EFGH", 2, 600));

        var result = await BuildController().Device(null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Token_returns_202_when_pending()
    {
        _cliAuth.Setup(s => s.PollTokenAsync("dev123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CliTokenResult.Waiting());

        var result = await BuildController().Token("dev123", CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status202Accepted);
    }

    [Fact]
    public async Task Token_creates_a_cli_session_when_approved()
    {
        _cliAuth.Setup(s => s.PollTokenAsync("dev123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CliTokenResult.Approved(CliSessionServiceTests.UserId, "laptop"));

        var result = await BuildController().Token("dev123", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value!;
        body.GetType().GetProperty("refreshToken")!.GetValue(body).Should().Be("plain-refresh");
        body.GetType().GetProperty("accessToken")!.GetValue(body).Should().Be("access-token");
        _svc.Sessions.Verify(r => r.AddAsync(It.Is<CliSession>(s => s.Name == "laptop"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Token_for_a_deactivated_approver_is_401()
    {
        _svc.User.IsActive = false;
        _cliAuth.Setup(s => s.PollTokenAsync("dev123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CliTokenResult.Approved(CliSessionServiceTests.UserId, null));

        var result = await BuildController().Token("dev123", CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Token_without_device_code_is_bad_request()
    {
        var result = await BuildController().Token("", CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Approve_unauthenticated_returns_401()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(false);

        var result = await BuildController().Approve(new CliApproveRequest("ABCD-EFGH"), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _cliAuth.Verify(s => s.ApproveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_with_an_api_key_is_403()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.IsHumanUser).Returns(false);
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var result = await BuildController().Approve(new CliApproveRequest("ABCD-EFGH"), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        _cliAuth.Verify(s => s.ApproveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_binds_the_signed_in_person()
    {
        SignedInPerson();
        _cliAuth.Setup(s => s.ApproveAsync("ABCD-EFGH", CliSessionServiceTests.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await BuildController().Approve(new CliApproveRequest(" ABCD-EFGH "), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _cliAuth.Verify(s => s.ApproveAsync("ABCD-EFGH", CliSessionServiceTests.UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_unknown_code_returns_bad_request()
    {
        SignedInPerson();
        _cliAuth.Setup(s => s.ApproveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await BuildController().Approve(new CliApproveRequest("BAD-CODE"), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Pending_returns_the_client_name_or_404()
    {
        SignedInPerson();
        _cliAuth.Setup(s => s.GetPendingClientNameAsync("ABCD-EFGH", It.IsAny<CancellationToken>())).ReturnsAsync("laptop");

        (await BuildController().Pending("ABCD-EFGH", CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        (await BuildController().Pending("ZZZZ-ZZZZ", CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Refresh_with_an_ended_session_is_401()
    {
        var result = await BuildController().Refresh(new CliRefreshRequest("plain-unknown"), CancellationToken.None);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Refresh_returns_a_new_access_token()
    {
        _svc.Sessions.Setup(r => r.GetByHashAsync("hash-of-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(_svc.LiveSession());

        var result = await BuildController().Refresh(new CliRefreshRequest("plain-refresh"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Logout_ends_the_session_and_revokes_the_bearer_token()
    {
        _svc.Sessions.Setup(r => r.GetByHashAsync("hash-of-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(_svc.LiveSession());
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString();

        var result = await BuildController(new Claim("jti", "jti-bearer"), new Claim("exp", exp))
            .Logout(new CliRefreshRequest("plain-refresh"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _svc.Revocation.Verify(r => r.RevokeTokenAsync("jti-old", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _svc.Revocation.Verify(r => r.RevokeTokenAsync("jti-bearer", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sessions_are_people_only()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.IsHumanUser).Returns(false);

        (await BuildController().ListSessions(CancellationToken.None))
            .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await BuildController().RevokeSession(Guid.NewGuid(), CancellationToken.None))
            .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task RevokeSession_unknown_is_404()
    {
        SignedInPerson();
        (await BuildController().RevokeSession(Guid.NewGuid(), CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
    }
}
