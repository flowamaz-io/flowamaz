using FluentAssertions;
using Flowamaz.Api.Controllers;
using Flowamaz.Application.Workflow.Gates;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Connectors.Gates;

/// <summary>
/// Tests HMAC gate link validation: valid signatures approve, invalid/expired/unknown reject with 400.
/// </summary>
[Trait("Category", "Gates")]
public class GateApprovalControllerTests
{
    private const string TestSigningKey = "test-gate-signing-key-32-chars!!";

    private static IConfiguration BuildConfig(string? gateKey = TestSigningKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GATE_SIGNING_KEY"] = gateKey
            })
            .Build();

    private static Mock<IHostEnvironment> BuildDevEnv()
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        return env;
    }

    private GateApprovalController BuildController(
        IGateDecisionRepository gateRepo,
        IWorkflowEventRepository? events = null,
        IWorkflowOrchestrator? orchestrator = null,
        IUnitOfWork? uow = null,
        IConfiguration? config = null,
        IHostEnvironment? env = null)
    {
        var gateService = new GateService(
            gateRepo,
            events ?? Mock.Of<IWorkflowEventRepository>(),
            orchestrator ?? Mock.Of<IWorkflowOrchestrator>(),
            uow ?? Mock.Of<IUnitOfWork>(),
            NullLogger<GateService>.Instance);

        var controller = new GateApprovalController(
            gateService,
            Mock.Of<ICurrentUserService>(),
            config ?? BuildConfig(),
            env ?? BuildDevEnv().Object,
            NullLogger<GateApprovalController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public async Task ApproveEmailLink_ValidHmac_Returns200WithSuccessHtml()
    {
        // Arrange
        var gateId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        const string nodeId = "approval-node";
        var expiresAt = DateTime.UtcNow.AddHours(48);
        var expiresUnix = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();

        var gateDecision = new GateDecision
        {
            Id = gateId, WorkspaceId = workspaceId, InstanceId = instanceId,
            NodeId = nodeId, Decision = GateDecisionStatus.Pending, ExpiresAt = expiresAt
        };

        var gateRepo = new Mock<IGateDecisionRepository>();
        gateRepo.Setup(r => r.GetByIdAsync(gateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateDecision);
        gateRepo.Setup(r => r.GetByNodeAsync(instanceId, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateDecision);

        var events = new Mock<IWorkflowEventRepository>();
        events.Setup(e => e.GetNextSequenceNumberAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var uow = new Mock<IUnitOfWork>();
        var orchestrator = new Mock<IWorkflowOrchestrator>();

        var message = $"{gateId}:approve:{expiresUnix}";
        var sig = GateHmacHelper.BuildHmac(message, TestSigningKey);

        var controller = BuildController(gateRepo.Object, events.Object, orchestrator.Object, uow.Object);

        // Act
        var result = await controller.ApproveViaEmail(gateId, sig, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ContentResult>();
        result.StatusCode.Should().Be(200);
        result.Content.Should().Contain("Decision Recorded");
    }

    [Fact]
    public async Task ApproveEmailLink_InvalidHmac_Returns400()
    {
        // Arrange
        var gateId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddHours(48);

        var gateDecision = new GateDecision
        {
            Id = gateId, WorkspaceId = workspaceId, InstanceId = instanceId,
            NodeId = "node", Decision = GateDecisionStatus.Pending, ExpiresAt = expiresAt
        };

        var gateRepo = new Mock<IGateDecisionRepository>();
        gateRepo.Setup(r => r.GetByIdAsync(gateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateDecision);

        var controller = BuildController(gateRepo.Object);

        // Act — tampered signature
        var result = await controller.ApproveViaEmail(gateId, "invalid-sig-tampered", CancellationToken.None);

        // Assert
        result.Should().BeOfType<ContentResult>();
        result.StatusCode.Should().Be(400);
        result.Content.Should().Contain("Link Invalid or Expired");
    }

    [Fact]
    public async Task ApproveEmailLink_ExpiredHmac_Returns400()
    {
        // Arrange
        var gateId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        // ExpiresAt is in the past
        var expiresAt = DateTime.UtcNow.AddHours(-1);
        var expiresUnix = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();

        var gateDecision = new GateDecision
        {
            Id = gateId, WorkspaceId = workspaceId, InstanceId = instanceId,
            NodeId = "node", Decision = GateDecisionStatus.Pending, ExpiresAt = expiresAt
        };

        var gateRepo = new Mock<IGateDecisionRepository>();
        gateRepo.Setup(r => r.GetByIdAsync(gateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gateDecision);

        // HMAC is valid but link is expired
        var message = $"{gateId}:approve:{expiresUnix}";
        var sig = GateHmacHelper.BuildHmac(message, TestSigningKey);

        var controller = BuildController(gateRepo.Object);

        // Act
        var result = await controller.ApproveViaEmail(gateId, sig, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ContentResult>();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ApproveEmailLink_GateNotFound_Returns400()
    {
        // Arrange — gate ID doesn't exist in any workspace
        var gateId = Guid.NewGuid();
        var gateRepo = new Mock<IGateDecisionRepository>();
        gateRepo.Setup(r => r.GetByIdAsync(gateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GateDecision?)null);

        var controller = BuildController(gateRepo.Object);
        var sig = GateHmacHelper.BuildHmac($"{gateId}:approve:0", TestSigningKey);

        // Act
        var result = await controller.ApproveViaEmail(gateId, sig, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ContentResult>();
        result.StatusCode.Should().Be(400);
    }
}
