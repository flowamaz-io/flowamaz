using System.Text;
using Flowamaz.Api.Controllers;
using Flowamaz.Core.Interfaces.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Webhooks;

public class WebhookReceiveControllerTests
{
    private readonly Mock<IWebhookService> _webhooks = new();
    private readonly Mock<IRateLimitService> _rateLimit = new();
    private static readonly Guid Endpoint = Guid.NewGuid();

    public WebhookReceiveControllerTests()
    {
        _rateLimit.Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private WebhookReceiveController CreateController(string body, string? signature)
    {
        var controller = new WebhookReceiveController(
            _webhooks.Object, _rateLimit.Object, NullLogger<WebhookReceiveController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.ContentLength = body.Length;
        if (signature is not null)
            httpContext.Request.Headers["X-Flowamaz-Signature"] = signature;

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task Receive_valid_signature_returns_202_with_instanceId()
    {
        var instanceId = Guid.NewGuid();
        _webhooks.Setup(w => w.TriggerFromWebhookAsync(
                Endpoint, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookTriggerResult(instanceId, "Pending", false));

        var result = await CreateController("{}", "validsig").Receive(Endpoint, sync: false, CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task Receive_invalid_signature_returns_401()
    {
        _webhooks.Setup(w => w.TriggerFromWebhookAsync(
                Endpoint, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("bad signature"));

        var result = await CreateController("{}", "wrongsig").Receive(Endpoint, sync: false, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Receive_missing_signature_returns_401()
    {
        _webhooks.Setup(w => w.TriggerFromWebhookAsync(
                Endpoint, It.IsAny<string>(), null, It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("missing signature"));

        var result = await CreateController("{}", signature: null).Receive(Endpoint, sync: false, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Receive_rate_limited_returns_429()
    {
        _rateLimit.Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateController("{}", "sig").Receive(Endpoint, sync: false, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }
}
