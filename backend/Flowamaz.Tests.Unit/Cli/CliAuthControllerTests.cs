using FluentAssertions;
using Flowamaz.Api.Controllers;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Flowamaz.Tests.Unit.Cli;

/// <summary>
/// CliAuthController device flow (prompt 05-02): device initiation, token polling (202 pending /
/// 200 ready), and browser approval binding the caller's bearer token.
/// </summary>
public class CliAuthControllerTests
{
    private readonly Mock<ICliAuthService> _cliAuth = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private CliAuthController BuildController(string? authorizationHeader = null)
    {
        var controller = new CliAuthController(_cliAuth.Object, _currentUser.Object);
        var httpContext = new DefaultHttpContext();
        if (authorizationHeader is not null)
            httpContext.Request.Headers.Authorization = authorizationHeader;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task Device_returns_authorization_with_codes()
    {
        _cliAuth.Setup(s => s.InitiateDeviceFlowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliDeviceAuthorization("dev123", "ABCD-EFGH", "https://app/cli-auth", "https://app/cli-auth?code=ABCD-EFGH", 2, 600));

        var result = await BuildController().Device(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<CliDeviceAuthorization>().Which.UserCode.Should().Be("ABCD-EFGH");
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
    public async Task Token_returns_200_with_token_when_ready()
    {
        var expires = DateTime.UtcNow.AddMinutes(15);
        _cliAuth.Setup(s => s.PollTokenAsync("dev123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CliTokenResult.Ready("the-access-token", expires));

        var result = await BuildController().Token("dev123", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
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
        _cliAuth.Verify(s => s.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_binds_caller_bearer_token_and_returns_ok()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _cliAuth.Setup(s => s.ApproveAsync("ABCD-EFGH", "caller-token", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await BuildController("Bearer caller-token").Approve(new CliApproveRequest("ABCD-EFGH"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _cliAuth.Verify(s => s.ApproveAsync("ABCD-EFGH", "caller-token", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_unknown_code_returns_bad_request()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _cliAuth.Setup(s => s.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await BuildController("Bearer caller-token").Approve(new CliApproveRequest("BAD-CODE"), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
