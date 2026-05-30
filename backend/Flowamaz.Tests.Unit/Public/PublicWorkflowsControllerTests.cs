using System.Text.Json;
using Flowamaz.Api.Controllers.Public;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Flowamaz.Tests.Unit.Public;

public class PublicWorkflowsControllerTests
{
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowOrchestrator> _orchestrator = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IPublicApiRateLimiter> _rateLimiter = new();

    private static readonly Guid Workspace = Guid.NewGuid();

    public PublicWorkflowsControllerTests()
    {
        _currentUser.SetupGet(c => c.IsApiKey).Returns(true);
        _currentUser.SetupGet(c => c.ApiKeyWorkspaceId).Returns(Workspace);
        _rateLimiter.Setup(r => r.CheckAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicApiRateLimit(true, 1000, 999, 1_700_000_000));
    }

    private PublicWorkflowsController CreateController()
    {
        var controller = new PublicWorkflowsController(
            _definitions.Object, _instances.Object, _orchestrator.Object, _currentUser.Object, _rateLimiter.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static string Serialize(object? value) => JsonSerializer.Serialize(value, new JsonSerializerOptions());

    [Fact]
    public async Task List_with_valid_api_key_returns_only_published_workflows()
    {
        _definitions.Setup(d => d.GetForWorkspaceAsync(Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WorkflowDefinition { Id = Guid.NewGuid(), WorkspaceId = Workspace, Name = "Live", Slug = "live-flow", Status = WorkflowStatus.Published },
                new WorkflowDefinition { Id = Guid.NewGuid(), WorkspaceId = Workspace, Name = "Draft", Slug = "draft-flow", Status = WorkflowStatus.Draft },
            ]);

        var result = await CreateController().List(null, null, CancellationToken.None);

        var json = result.Should().BeOfType<JsonResult>().Which;
        json.StatusCode.Should().Be(200);
        var body = Serialize(json.Value);
        body.Should().Contain("live-flow");
        body.Should().NotContain("draft-flow");
    }

    [Fact]
    public async Task Trigger_with_unknown_slug_returns_404_with_error_body()
    {
        _definitions.Setup(d => d.GetBySlugForWorkspaceAsync(Workspace, "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        var result = await CreateController().Trigger("missing", CancellationToken.None);

        var json = result.Should().BeOfType<JsonResult>().Which;
        json.StatusCode.Should().Be(404);
        Serialize(json.Value).Should().Contain("workflow_not_found");
    }

    [Fact]
    public async Task List_without_api_key_returns_401_with_sdk_hint()
    {
        _currentUser.SetupGet(c => c.IsApiKey).Returns(false);

        var result = await CreateController().List(null, null, CancellationToken.None);

        var json = result.Should().BeOfType<JsonResult>().Which;
        json.StatusCode.Should().Be(401);
        var body = Serialize(json.Value);
        body.Should().Contain("unauthorized");
        body.Should().Contain("settings/api-keys");
    }

    [Fact]
    public async Task List_when_rate_limit_exceeded_returns_429_with_reset_header()
    {
        _rateLimiter.Setup(r => r.CheckAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicApiRateLimit(false, 1000, 0, 1_700_000_000));

        var controller = CreateController();
        var result = await controller.List(null, null, CancellationToken.None);

        var json = result.Should().BeOfType<JsonResult>().Which;
        json.StatusCode.Should().Be(429);
        Serialize(json.Value).Should().Contain("rate_limited");
        controller.Response.Headers.Should().ContainKey("X-RateLimit-Reset");
    }
}
