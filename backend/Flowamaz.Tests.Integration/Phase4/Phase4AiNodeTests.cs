using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Application.Workflow.Workers;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Integration.Phase4;

/// <summary>
/// Phase 4 AI node worker tests — run as service-level tests (mocked AI, no HTTP).
/// These complement the existing unit tests in Flowamaz.Tests.Unit.Workflow.Workers.
/// Placed here because the prompt mandates Phase4 integration-level coverage.
/// </summary>
[Collection("api")]
public class Phase4AiNodeTests : ApiTestBase
{
    public Phase4AiNodeTests(IntegrationApiFixture fixture) : base(fixture) { }

    private readonly Mock<IAiCompletionService> _completion = new();
    private readonly Mock<IModelResolutionService> _modelResolution = new();
    private readonly Mock<IAiTokenMeteringService> _metering = new();
    private readonly Mock<IVariableEvaluationService> _variableEvaluation = new();
    private readonly Mock<ICredentialVaultService> _vault = new();
    private readonly Mock<IByomProviderService> _byom = new();

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
            _variableEvaluation.Object, _vault.Object, _byom.Object,
            NullLogger<AiNodeWorker>.Instance);

    private NodeExecutionContext BuildContext(
        JsonDocument nodeConfig,
        IReadOnlyDictionary<string, JsonElement>? variables = null,
        IReadOnlySet<string>? sensitiveNames = null)
    {
        var node = new SfgNode(
            Id: "ai-p4",
            Type: NodeType.Ai,
            Label: "Phase4 AI",
            Config: nodeConfig,
            RetryPolicy: null,
            TimeoutPolicy: null,
            Compensation: null);

        var graph = new WorkflowGraph(
            WorkflowId: "p4-wf",
            Version: "v1",
            Nodes: [node],
            Edges: [],
            Metadata: new WorkflowMetadata("Phase4", null));

        return new NodeExecutionContext(
            InstanceId: Guid.NewGuid(),
            WorkspaceId: Guid.NewGuid(),
            Node: node,
            Graph: graph,
            Variables: variables ?? new Dictionary<string, JsonElement>(),
            LeaseId: "p4-lease",
            SensitiveVariableNames: sensitiveNames);
    }

    private void SetupDefaultModel()
    {
        _modelResolution.Setup(m => m.ResolveModelConfigAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultConfig);
    }

    private void SetupPassThroughInterpolate()
    {
        _variableEvaluation.Setup(v => v.InterpolateAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, CancellationToken>((_, t, _) => Task.FromResult(t));
    }

    // ── S-INT-1: Sensitive variable NOT in AI prompt ──────────────────────────

    [Fact]
    public async Task AiNodeWorker_SensitiveVariable_NotInPrompt()
    {
        SetupDefaultModel();
        SetupPassThroughInterpolate();

        string? capturedPrompt = null;
        _completion.Setup(c => c.CompleteAsync(
                It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<ModelConfig, string, string, CancellationToken>((_, _, prompt, _) =>
            {
                capturedPrompt = prompt;
                return Task.FromResult(new AiCompletionResult("ok", 10, 5));
            });

        var variables = new Dictionary<string, JsonElement>
        {
            ["secret_key"] = JsonDocument.Parse("\"sk-verysecret\"").RootElement,
            ["normal_var"] = JsonDocument.Parse("\"hello\"").RootElement,
        };
        var sensitiveNames = new HashSet<string> { "secret_key" };
        var config = JsonDocument.Parse("""{"prompt_template":"key={{ secret_key }} val={{ normal_var }}"}""");
        var context = BuildContext(config, variables, sensitiveNames);

        var result = await BuildWorker().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedPrompt.Should().NotContain("sk-verysecret");
        capturedPrompt.Should().Contain("[SENSITIVE_REDACTED]");
        capturedPrompt.Should().Contain("hello");
    }

    // ── S-INT-2: Output schema mismatch triggers self-correction retry ─────────

    [Fact]
    public async Task AiNodeWorker_OutputSchema_InvalidResponse_SelfCorrectionRetried()
    {
        SetupDefaultModel();
        SetupPassThroughInterpolate();

        var callCount = 0;
        _completion.Setup(c => c.CompleteAsync(
                It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<ModelConfig, string, string, CancellationToken>((_, _, _, _) =>
            {
                callCount++;
                // First call: invalid JSON; second: valid JSON matching the schema
                var text = callCount == 1 ? "not valid json" : """{"result":"ok"}""";
                return Task.FromResult(new AiCompletionResult(text, 10, 5));
            });

        // Schema requires a "result" property
        var outputSchema = """{"type":"object","properties":{"result":{"type":"string"}},"required":["result"]}""";
        var config = JsonDocument.Parse($$$"""{"prompt_template":"produce output","output_schema":{{{outputSchema}}}}""");
        var context = BuildContext(config);

        var result = await BuildWorker().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        callCount.Should().Be(2, "self-correction retry should be made after first invalid response");
    }

    // ── S-INT-3: Cost cap prevents AI call ────────────────────────────────────

    [Fact]
    public async Task AiNodeWorker_CostCap_PreventCall()
    {
        SetupDefaultModel();
        SetupPassThroughInterpolate();

        // cost_cap_usd of 0.0001 is smaller than the estimated cost of a 200-word prompt
        var longPrompt = string.Join(" ", Enumerable.Repeat("word", 200));
        var config = JsonDocument.Parse(
            $$$"""{"prompt_template":"{{{longPrompt}}}","cost_cap_usd":0.0001}""");
        var context = BuildContext(config);

        var result = await BuildWorker().ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage!.ToLowerInvariant().Should().Contain("cost cap");
        _completion.Verify(c => c.CompleteAsync(
            It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
