using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Application.Workflow.Workers;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Workers;

/// <summary>
/// AiNodeWorker behaviour: sensitive variable redaction, fallback chain, output schema
/// self-correction, cost cap gate, and BYOM routing.
/// </summary>
public class AiNodeWorkerTests
{
    private readonly Mock<IAiCompletionService> _completion = new();
    private readonly Mock<IModelResolutionService> _modelResolution = new();
    private readonly Mock<IAiTokenMeteringService> _metering = new();
    private readonly Mock<IVariableEvaluationService> _variableEval = new();
    private readonly Mock<ICredentialVaultService> _vault = new();
    private readonly Mock<IByomProviderService> _byom = new();

    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _instanceId = Guid.NewGuid();

    private static readonly ModelConfig DefaultConfig = new(
        ModelId: "claude-haiku-4-5",
        Provider: "anthropic",
        ApiKeySource: AiKeySource.Platform,
        ApiKey: "key",
        HasVision: false,
        MaxContextTokens: 100_000,
        SupportsJsonMode: true,
        SupportsStreaming: false);

    private AiNodeWorker BuildWorker() =>
        new(_completion.Object, _modelResolution.Object, _metering.Object,
            _variableEval.Object, _vault.Object, _byom.Object,
            NullLogger<AiNodeWorker>.Instance);

    private NodeExecutionContext BuildContext(
        JsonDocument nodeConfig,
        IReadOnlyDictionary<string, JsonElement>? variables = null,
        IReadOnlySet<string>? sensitiveNames = null)
    {
        var node = new SfgNode(
            Id: "ai-node-1",
            Type: NodeType.Ai,
            Label: "AI Node",
            Config: nodeConfig,
            RetryPolicy: null,
            TimeoutPolicy: null,
            Compensation: null);

        var graph = new WorkflowGraph(
            WorkflowId: "wf1",
            Version: "v1",
            Nodes: [node],
            Edges: [],
            Metadata: new WorkflowMetadata("Test", null));

        return new NodeExecutionContext(
            InstanceId: _instanceId,
            WorkspaceId: _workspaceId,
            Node: node,
            Graph: graph,
            Variables: variables ?? new Dictionary<string, JsonElement>(),
            LeaseId: "lease-1",
            SensitiveVariableNames: sensitiveNames);
    }

    private void SetupDefaultModelResolution()
    {
        _modelResolution.Setup(m => m.ResolveModelConfigAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultConfig);
    }

    private void SetupPassThroughInterpolate()
    {
        _variableEval.Setup(v => v.InterpolateAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, CancellationToken>((_, template, _) => Task.FromResult(template));
    }

    // ── Test 1: Sensitive variables are stripped before AI call ───────────────

    [Fact]
    public async Task ExecuteAsync_SensitiveVariables_AreRedactedBeforeAiCall()
    {
        SetupDefaultModelResolution();
        SetupPassThroughInterpolate();

        string? capturedPrompt = null;
        _completion.Setup(c => c.CompleteAsync(
                It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<ModelConfig, string, string, CancellationToken>((_, _, prompt, _) =>
            {
                capturedPrompt = prompt;
                return Task.FromResult(new AiCompletionResult("done", 10, 5));
            });

        var variables = new Dictionary<string, JsonElement>
        {
            ["password"] = JsonDocument.Parse("\"secret123\"").RootElement,
            ["api_key"] = JsonDocument.Parse("\"sk-xxx\"").RootElement,
            ["name"] = JsonDocument.Parse("\"Alice\"").RootElement,
        };

        var sensitiveNames = new HashSet<string> { "password", "api_key" };

        var configJson = """{"prompt_template":"Hello {{ name }}, pwd={{ password }}, key={{ api_key }}"}""";
        var context = BuildContext(JsonDocument.Parse(configJson), variables, sensitiveNames);

        var worker = BuildWorker();
        var result = await worker.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedPrompt.Should().NotBeNull();
        capturedPrompt.Should().NotContain("secret123");
        capturedPrompt.Should().NotContain("sk-xxx");
        capturedPrompt.Should().Contain("[SENSITIVE_REDACTED]");
        capturedPrompt.Should().Contain("Alice");
    }

    // ── Test 2: Fallback chain — primary throws, fallback succeeds ────────────

    [Fact]
    public async Task ExecuteAsync_PrimaryFails_FallbackModelSucceeds()
    {
        SetupDefaultModelResolution();
        SetupPassThroughInterpolate();

        var callCount = 0;
        _completion.Setup(c => c.CompleteAsync(
                It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<ModelConfig, string, string, CancellationToken>((_, _, _, _) =>
            {
                callCount++;
                if (callCount == 1) throw new HttpRequestException("Primary model unavailable");
                return Task.FromResult(new AiCompletionResult("{\"ok\":true}", 10, 5));
            });

        var configJson = """{"prompt_template":"do something","fallback_models":["claude-sonnet-4-6"]}""";
        var context = BuildContext(JsonDocument.Parse(configJson));

        var worker = BuildWorker();
        var result = await worker.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        callCount.Should().Be(2, "primary failed so fallback should be used");
    }

    // ── Test 3: Schema validation fails → self-correction retry succeeds ──────

    [Fact]
    public async Task ExecuteAsync_OutputSchemaMismatch_SelfCorrectionSucceeds()
    {
        SetupDefaultModelResolution();
        SetupPassThroughInterpolate();

        var callCount = 0;
        _completion.Setup(c => c.CompleteAsync(
                It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<ModelConfig, string, string, CancellationToken>((_, _, _, _) =>
            {
                callCount++;
                return Task.FromResult(callCount == 1
                    ? new AiCompletionResult("not json at all", 10, 5)    // invalid
                    : new AiCompletionResult("""{"name":"Alice"}""", 10, 5)); // valid
            });

        // Schema requires { "name": string }
        var schemaJson = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""";
        var configJson = $$"""{"prompt_template":"classify this","output_schema":{{schemaJson}}}""";
        var context = BuildContext(JsonDocument.Parse(configJson));

        var worker = BuildWorker();
        var result = await worker.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        callCount.Should().Be(2, "first attempt invalid, second self-correction should succeed");
    }

    // ── Test 4: Both primary and self-correction fail → Fatal result ──────────

    [Fact]
    public async Task ExecuteAsync_SelfCorrectionAlsoFails_ReturnsFatal()
    {
        SetupDefaultModelResolution();
        SetupPassThroughInterpolate();

        // Both attempts return invalid JSON (missing required "name" field).
        _completion.Setup(c => c.CompleteAsync(
                It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<ModelConfig, string, string, CancellationToken>((_, _, _, _) =>
                Task.FromResult(new AiCompletionResult("""{"wrong_field":1}""", 10, 5)));

        var schemaJson = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""";
        var configJson = $$"""{"prompt_template":"classify this","output_schema":{{schemaJson}}}""";
        var context = BuildContext(JsonDocument.Parse(configJson));

        var worker = BuildWorker();
        var result = await worker.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse("schema mismatch after self-correction is Fatal");
        result.ErrorMessage.Should().Contain("schema");
    }

    // ── Test 5: Cost cap exceeded → Fatal before any AI call ─────────────────

    [Fact]
    public async Task ExecuteAsync_CostCapExceeded_ReturnsFatalWithoutCallingAi()
    {
        SetupDefaultModelResolution();
        SetupPassThroughInterpolate();

        // cost_cap_usd: 0.000001 (effectively zero) — any prompt will exceed this.
        // Large prompt so estimated cost > 0.000001.
        var largePrompt = new string('a', 5000); // ~1250 tokens input alone.
        var configJson = $$"""{"prompt_template":"{{largePrompt}}","cost_cap_usd":0.000001}""";
        var context = BuildContext(JsonDocument.Parse(configJson));

        var worker = BuildWorker();
        var result = await worker.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cost cap");

        _completion.Verify(c => c.CompleteAsync(
            It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Test 6: BYOM provider used when provider = "byom" ────────────────────

    [Fact]
    public async Task ExecuteAsync_ByomProvider_CallsByomServiceNotCompletionService()
    {
        SetupDefaultModelResolution();
        SetupPassThroughInterpolate();

        var credentialId = Guid.NewGuid();

        _byom.Setup(b => b.CompleteAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"result":"from byom"}""");

        var configJson = $$"""{"prompt_template":"test","provider":"byom","model":"custom-model","byom_credential_id":"{{credentialId}}"}""";
        var context = BuildContext(JsonDocument.Parse(configJson));

        var worker = BuildWorker();
        var result = await worker.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();

        _byom.Verify(b => b.CompleteAsync(
            _workspaceId, credentialId, "custom-model",
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _completion.Verify(c => c.CompleteAsync(
            It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
