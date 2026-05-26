using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Flowamaz.Api.Controllers;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Connectors.Gates;

/// <summary>
/// Tests Slack interactive-action signature validation: valid sigs process, invalid reject 401,
/// missing secret returns 503 in non-Development environments.
/// </summary>
[Trait("Category", "Gates")]
public class SlackActionsControllerTests
{
    private const string TestSlackSecret = "test-slack-signing-secret-abc";

    private static GateApprovalController BuildController(
        string? slackSecret,
        bool isDevelopment = false,
        IGateDecisionRepository? gateRepo = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GATE_SIGNING_KEY"] = "test-gate-key-32-chars!!!!!!!!!!",
                ["SLACK_SIGNING_SECRET"] = slackSecret
            })
            .Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName)
            .Returns(isDevelopment ? Environments.Development : Environments.Production);

        var gateService = new GateService(
            gateRepo ?? Mock.Of<IGateDecisionRepository>(),
            Mock.Of<IWorkflowEventRepository>(),
            Mock.Of<IWorkflowOrchestrator>(),
            Mock.Of<IUnitOfWork>(),
            NullLogger<GateService>.Instance);

        return new GateApprovalController(
            gateService,
            Mock.Of<ICurrentUserService>(),
            config,
            envMock.Object,
            NullLogger<GateApprovalController>.Instance);
    }

    private static (HttpContext ctx, string body) BuildSlackHttpContext(string signingSecret, string payloadJson)
    {
        var body = $"payload={Uri.EscapeDataString(payloadJson)}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var sigBase = $"v0:{timestamp}:{body}";
        var keyBytes = Encoding.UTF8.GetBytes(signingSecret);
        var msgBytes = Encoding.UTF8.GetBytes(sigBase);
        var hash = HMACSHA256.HashData(keyBytes, msgBytes);
        var sig = $"v0={Convert.ToHexString(hash).ToLowerInvariant()}";

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Headers["X-Slack-Request-Timestamp"] = timestamp;
        ctx.Request.Headers["X-Slack-Signature"] = sig;

        var formData = $"payload={Uri.EscapeDataString(payloadJson)}";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(formData));
        ctx.Request.Body.Position = 0;
        ctx.Request.ContentLength = formData.Length;

        // Set up form collection
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["payload"] = payloadJson
        });
        ctx.Request.Form = form;

        return (ctx, body);
    }

    [Fact]
    public async Task SlackAction_ValidSignature_Returns200()
    {
        // Arrange
        var gateId = Guid.NewGuid();
        var payloadJson = $$"""{"actions":[{"action_id":"approve_gate","value":"{{gateId}}"}]}""";

        var controller = BuildController(TestSlackSecret);
        var (ctx, _) = BuildSlackHttpContext(TestSlackSecret, payloadJson);
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };

        // Act
        var result = await controller.SlackActions(CancellationToken.None);

        // Assert — gate not found returns Ok (idempotent), not 401/503
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SlackAction_InvalidSignature_Returns401()
    {
        // Arrange — use wrong signing secret to build the request
        const string wrongSecret = "wrong-secret-that-does-not-match";
        var gateId = Guid.NewGuid();
        var payloadJson = $$"""{"actions":[{"action_id":"approve_gate","value":"{{gateId}}"}]}""";

        var controller = BuildController(TestSlackSecret); // controller uses TestSlackSecret
        var (ctx, _) = BuildSlackHttpContext(wrongSecret, payloadJson); // request signed with wrong secret
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };

        // Act
        var result = await controller.SlackActions(CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task SlackAction_MissingSecretInDevelopment_AllowsThrough()
    {
        // Arrange — no SLACK_SIGNING_SECRET, but running in Development
        var gateId = Guid.NewGuid();
        var payloadJson = $$"""{"actions":[{"action_id":"approve_gate","value":"{{gateId}}"}]}""";

        var controller = BuildController(slackSecret: null, isDevelopment: true);
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["payload"] = payloadJson
        });
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };

        // Act
        var result = await controller.SlackActions(CancellationToken.None);

        // Assert — dev bypass allows through (gate not found = Ok, not 401 or 503)
        result.Should().NotBeOfType<UnauthorizedResult>();
        result.Should().NotBeOfType<StatusCodeResult>(
            because: "should not return 503 in Development");
    }

    [Fact]
    public async Task SlackAction_MissingSecretInProduction_Returns503()
    {
        // Arrange — no SLACK_SIGNING_SECRET in Production
        var controller = BuildController(slackSecret: null, isDevelopment: false);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Act
        var result = await controller.SlackActions(CancellationToken.None);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(503);
    }
}
