using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Workflow.Gates;
using Flowamaz.Application.Workflow.Workers;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Workers;

/// <summary>
/// HumanGateNodeWorker: creates GateDecision + returns GateWaiting result, delivers via
/// Slack/Teams channels, and validates HMAC signing helpers.
/// </summary>
public class HumanGateNodeWorkerTests
{
    private readonly Mock<IGateDecisionRepository> _gateRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IConnectorOperationHandlerRegistry> _registry = new();
    private readonly IConfiguration _config;

    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();

    public HumanGateNodeWorkerTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GATE_SIGNING_KEY"] = "test-signing-key",
                ["PLATFORM_URL"] = "https://app.flowamaz.test",
            })
            .Build();
    }

    private HumanGateNodeWorker BuildWorker() =>
        new(_gateRepo.Object, _unitOfWork.Object, _email.Object,
            _registry.Object, _config, NullLogger<HumanGateNodeWorker>.Instance);

    private NodeExecutionContext BuildContext(
        string deliveryChannel = "Portal",
        string? assignedToVariable = null,
        Dictionary<string, JsonElement>? variables = null)
    {
        var configJson = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            delivery_channel = deliveryChannel,
            gate_label = "Finance Approval",
            gate_summary = "Budget exceeds threshold",
            timeout_hours = 24,
            assigned_to_variable = assignedToVariable,
            slack_channel = "#approvals",
            teams_webhook_url = "https://teams.example.com/webhook",
            workflow_name = "Invoice Processing",
        }));

        var node = new SfgNode(
            Id: "gate-1",
            Type: NodeType.HumanGate,
            Label: "Finance Gate",
            Config: configJson,
            RetryPolicy: null,
            TimeoutPolicy: null,
            Compensation: null);

        var graph = new WorkflowGraph(
            "wf-1", "1.0",
            [node],
            [],
            new WorkflowMetadata("Invoice Processing", null));

        var vars = variables ?? new Dictionary<string, JsonElement>();

        return new NodeExecutionContext(
            _instanceId, _workspaceId, node, graph,
            vars, "lease-1");
    }

    [Fact]
    public async Task ExecuteAsync_Portal_CreatesGateDecisionAndReturnsGateWaiting()
    {
        // Arrange
        GateDecision? captured = null;
        _gateRepo
            .Setup(r => r.AddAsync(It.IsAny<GateDecision>(), It.IsAny<CancellationToken>()))
            .Callback<GateDecision, CancellationToken>((g, _) => captured = g)
            .Returns(Task.CompletedTask);

        var worker = BuildWorker();
        var ctx = BuildContext("Portal");

        // Act
        var result = await worker.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.IsGateWaiting.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.GateDecisionId.Should().NotBeNullOrEmpty();

        captured.Should().NotBeNull();
        captured!.WorkspaceId.Should().Be(_workspaceId);
        captured.InstanceId.Should().Be(_instanceId);
        captured.NodeId.Should().Be("gate-1");
        captured.Decision.Should().Be(GateDecisionStatus.Pending);
        captured.DeliveryChannel.Should().Be(GateDeliveryChannel.Portal);
        captured.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_AssignedToVariable_ResolvesEmailFromVariables()
    {
        // Arrange
        GateDecision? captured = null;
        _gateRepo
            .Setup(r => r.AddAsync(It.IsAny<GateDecision>(), It.IsAny<CancellationToken>()))
            .Callback<GateDecision, CancellationToken>((g, _) => captured = g)
            .Returns(Task.CompletedTask);

        var vars = new Dictionary<string, JsonElement>
        {
            ["approver_email"] = JsonDocument.Parse("\"jane@example.com\"").RootElement,
        };

        var worker = BuildWorker();
        var ctx = BuildContext("Email", assignedToVariable: "approver_email", variables: vars);

        // Act
        var result = await worker.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.IsGateWaiting.Should().BeTrue();
        captured!.AssignedToEmail.Should().Be("jane@example.com");
    }

    [Fact]
    public async Task ExecuteAsync_SlackChannel_CallsSlackApprovalHandler()
    {
        // Arrange
        var slackHandler = new Mock<IConnectorOperationHandler>();
        slackHandler.Setup(h => h.ConnectorId).Returns("slack");
        slackHandler.Setup(h => h.OperationId).Returns("post-approval-message");
        slackHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<JsonDocument>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonDocument.Parse("{\"ok\":true}"));

        _registry
            .Setup(r => r.Get("slack", "post-approval-message"))
            .Returns(slackHandler.Object);

        _gateRepo
            .Setup(r => r.AddAsync(It.IsAny<GateDecision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var worker = BuildWorker();
        var ctx = BuildContext("Slack");

        // Act
        var result = await worker.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.IsGateWaiting.Should().BeTrue();

        slackHandler.Verify(
            h => h.ExecuteAsync(
                It.Is<JsonDocument>(d =>
                    d.RootElement.GetProperty("channel").GetString() == "#approvals"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TeamsChannel_CallsTeamsAdaptiveCardHandler()
    {
        // Arrange
        var teamsHandler = new Mock<IConnectorOperationHandler>();
        teamsHandler.Setup(h => h.ConnectorId).Returns("microsoft-teams");
        teamsHandler.Setup(h => h.OperationId).Returns("post-adaptive-card");
        teamsHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<JsonDocument>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonDocument.Parse("{\"success\":true}"));

        _registry
            .Setup(r => r.Get("microsoft-teams", "post-adaptive-card"))
            .Returns(teamsHandler.Object);

        _gateRepo
            .Setup(r => r.AddAsync(It.IsAny<GateDecision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var worker = BuildWorker();
        var ctx = BuildContext("Teams");

        // Act
        var result = await worker.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.IsGateWaiting.Should().BeTrue();

        teamsHandler.Verify(
            h => h.ExecuteAsync(
                It.Is<JsonDocument>(d =>
                    d.RootElement.GetProperty("webhook_url").GetString() == "https://teams.example.com/webhook"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void GateWaiting_Result_HasCorrectProperties()
    {
        // Arrange
        var gateId = Guid.NewGuid().ToString();

        // Act
        var result = NodeExecutionResult.GateWaiting(gateId);

        // Assert
        result.IsGateWaiting.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.GateDecisionId.Should().Be(gateId);
    }

    [Fact]
    public void Fatal_Result_IsNotGateWaiting()
    {
        var result = NodeExecutionResult.Fatal("some error");
        result.IsGateWaiting.Should().BeFalse();
        result.GateDecisionId.Should().BeNull();
    }

    [Fact]
    public void BuildHmac_SameInputs_ProduceSameSignature()
    {
        var sig1 = GateHmacHelper.BuildHmac("msg:approve:1234567890", "secret-key");
        var sig2 = GateHmacHelper.BuildHmac("msg:approve:1234567890", "secret-key");

        sig1.Should().Be(sig2);
        sig1.Should().HaveLength(64); // 32 bytes = 64 hex chars
    }

    [Fact]
    public void ValidateHmac_ValidSignature_ReturnsTrue()
    {
        const string key = "test-key";
        const string message = "gate-id:approve:1234567890";
        var sig = GateHmacHelper.BuildHmac(message, key);

        GateHmacHelper.ValidateHmac(message, sig, key).Should().BeTrue();
    }

    [Fact]
    public void ValidateHmac_WrongSignature_ReturnsFalse()
    {
        GateHmacHelper.ValidateHmac("message", "wrongsig", "key").Should().BeFalse();
    }
}
