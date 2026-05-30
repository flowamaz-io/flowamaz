using System.Security.Cryptography;
using System.Text;
using Flowamaz.Application.Webhooks;
using Flowamaz.Core.Entities.Webhooks;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Webhooks;

public class WebhookServiceTests
{
    private readonly Mock<IWebhookEndpointRepository> _endpoints = new();
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IWorkflowOrchestrator> _orchestrator = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly ISecretProtector _protector;

    private static readonly Guid Workspace = Guid.NewGuid();
    private static readonly Guid Workflow = Guid.NewGuid();
    private static readonly Guid Endpoint = Guid.NewGuid();

    public WebhookServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CREDENTIAL_MASTER_KEY"] = "test-master-key-at-least-32-chars-long!!",
            })
            .Build();
        _protector = new AesGcmSecretProtector(config);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private WebhookService CreateService() => new(
        _endpoints.Object, _definitions.Object, _instances.Object, _orchestrator.Object,
        _protector, _uow.Object, NullLogger<WebhookService>.Instance);

    private static string Sign(string secret, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

    [Fact]
    public async Task CreateEndpoint_stores_encrypted_secret_and_returns_plain_once()
    {
        _definitions.Setup(d => d.GetByIdForWorkspaceAsync(Workflow, Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinition { Id = Workflow, WorkspaceId = Workspace });
        WebhookEndpoint? stored = null;
        _endpoints.Setup(r => r.AddAsync(It.IsAny<WebhookEndpoint>(), It.IsAny<CancellationToken>()))
            .Callback<WebhookEndpoint, CancellationToken>((e, _) => stored = e)
            .Returns(Task.CompletedTask);

        var result = await CreateService().CreateEndpointAsync(Workspace, Workflow, "ci hook");

        result.Secret.Should().HaveLength(64);
        stored.Should().NotBeNull();
        stored!.EncryptedSecret.Should().NotBe(result.Secret);
        stored.EncryptedSecret.Should().NotContain(result.Secret);
        _protector.Unprotect(Workspace, stored.EncryptedSecret).Should().Be(result.Secret);
    }

    [Fact]
    public async Task CreateEndpoint_unknown_workflow_throws_actionable()
    {
        _definitions.Setup(d => d.GetByIdForWorkspaceAsync(Workflow, Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        var act = () => CreateService().CreateEndpointAsync(Workspace, Workflow, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*was not found in this workspace*");
    }

    [Fact]
    public async Task ValidateSignature_correct_signature_returns_true()
    {
        const string secret = "abc123";
        const string body = "{\"event\":\"order.created\"}";
        SetupActiveEndpoint(secret);

        var ok = await CreateService().ValidateSignatureAsync(Endpoint, body, Sign(secret, body));

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSignature_accepts_github_style_sha256_prefix()
    {
        const string secret = "abc123";
        const string body = "payload";
        SetupActiveEndpoint(secret);

        var ok = await CreateService().ValidateSignatureAsync(Endpoint, body, $"sha256={Sign(secret, body)}");

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSignature_tampered_body_returns_false()
    {
        const string secret = "abc123";
        SetupActiveEndpoint(secret);

        var signature = Sign(secret, "original");
        var ok = await CreateService().ValidateSignatureAsync(Endpoint, "tampered", signature);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateSignature_missing_header_returns_false()
    {
        SetupActiveEndpoint("abc123");

        var ok = await CreateService().ValidateSignatureAsync(Endpoint, "body", signatureHeader: null);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task TriggerFromWebhook_passes_idempotency_key_and_returns_same_instance()
    {
        const string secret = "abc123";
        const string body = "{\"x\":1}";
        SetupActiveEndpoint(secret);
        var instanceId = Guid.NewGuid();
        // Orchestrator dedups by idempotency key: same key → same instance.
        _orchestrator.Setup(o => o.TriggerAsync(
                Workspace, Workflow, body, "idem-1", InstanceTriggerType.Webhook, null, false, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowInstance { Id = instanceId, Status = InstanceStatus.Pending });

        var svc = CreateService();
        var first = await svc.TriggerFromWebhookAsync(Endpoint, body, Sign(secret, body), "idem-1");
        var second = await svc.TriggerFromWebhookAsync(Endpoint, body, Sign(secret, body), "idem-1");

        first.InstanceId.Should().Be(instanceId);
        second.InstanceId.Should().Be(instanceId);
        _orchestrator.Verify(o => o.TriggerAsync(
            Workspace, Workflow, body, "idem-1", InstanceTriggerType.Webhook, null, false, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task TriggerFromWebhook_invalid_signature_throws_unauthorized()
    {
        SetupActiveEndpoint("abc123");

        var act = () => CreateService().TriggerFromWebhookAsync(Endpoint, "body", "deadbeef", idempotencyKey: null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _orchestrator.Verify(o => o.TriggerAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<InstanceTriggerType>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupActiveEndpoint(string secret)
    {
        var endpoint = new WebhookEndpoint
        {
            Id = Endpoint,
            WorkspaceId = Workspace,
            WorkflowDefinitionId = Workflow,
            EncryptedSecret = _protector.Protect(Workspace, secret),
            IsActive = true,
        };
        _endpoints.Setup(r => r.FindByIdAsync(Endpoint, It.IsAny<CancellationToken>())).ReturnsAsync(endpoint);
    }
}
