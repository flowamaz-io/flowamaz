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

public class PublicInstancesControllerTests
{
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowEventRepository> _events = new();
    private readonly Mock<IWorkflowOrchestrator> _orchestrator = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IPublicApiRateLimiter> _rateLimiter = new();

    private static readonly Guid Workspace = Guid.NewGuid();

    public PublicInstancesControllerTests()
    {
        _currentUser.SetupGet(c => c.IsApiKey).Returns(true);
        _currentUser.SetupGet(c => c.ApiKeyWorkspaceId).Returns(Workspace);
        _rateLimiter.Setup(r => r.CheckAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicApiRateLimit(true, 1000, 999, 1_700_000_000));
    }

    private PublicInstancesController CreateController()
    {
        var controller = new PublicInstancesController(
            _instances.Object, _events.Object, _orchestrator.Object, _currentUser.Object, _rateLimiter.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public async Task Get_returns_snake_case_instance_response()
    {
        var instanceId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(instanceId, Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance
            {
                Id = instanceId, WorkspaceId = Workspace, WorkflowDefinitionId = workflowId,
                Status = InstanceStatus.Running, TriggerType = InstanceTriggerType.Manual,
            });

        var result = await CreateController().Get(instanceId, CancellationToken.None);

        var json = result.Should().BeOfType<JsonResult>().Which;
        json.StatusCode.Should().Be(200);
        // The response uses snake_case property names via PublicJson serializer settings.
        var serialized = JsonSerializer.Serialize(json.Value, (JsonSerializerOptions)json.SerializerSettings!);
        serialized.Should().Contain("workflow_definition_id");
        serialized.Should().Contain(workflowId.ToString());
    }

    [Fact]
    public async Task Get_missing_instance_returns_404()
    {
        var instanceId = Guid.NewGuid();
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(instanceId, Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        var result = await CreateController().Get(instanceId, CancellationToken.None);

        var json = result.Should().BeOfType<JsonResult>().Which;
        json.StatusCode.Should().Be(404);
        JsonSerializer.Serialize(json.Value, new JsonSerializerOptions()).Should().Contain("instance_not_found");
    }

    [Fact]
    public async Task Cancel_missing_instance_returns_404_and_does_not_cancel()
    {
        var instanceId = Guid.NewGuid();
        _instances.Setup(r => r.GetByIdForWorkspaceAsync(instanceId, Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        var result = await CreateController().Cancel(instanceId, CancellationToken.None);

        result.Should().BeOfType<JsonResult>().Which.StatusCode.Should().Be(404);
        _orchestrator.Verify(o => o.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
