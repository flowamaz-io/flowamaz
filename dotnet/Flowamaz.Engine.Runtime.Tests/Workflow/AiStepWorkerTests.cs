using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Workflow.Workers;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Runtime;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Engine.Runtime.Tests.Workflow;

/// <summary>The open AI step: the host's own key, mandatory metering, sensitive variables withheld.</summary>
public class AiStepWorkerTests
{
    private readonly Mock<IAiModelProvider> _provider = new();
    private readonly List<AiUsageRecord> _metered = [];
    private readonly Mock<IAiUsageMeter> _meter = new();
    private readonly Mock<IVariableEvaluationService> _interpolation = new();
    private readonly Mock<IWorkflowVariableRepository> _variables = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private AiCompletionRequest? _sent;

    public AiStepWorkerTests()
    {
        _provider.SetupGet(p => p.Name).Returns("anthropic");
        _provider.SetupGet(p => p.DefaultModel).Returns("configured-model");
        _provider.Setup(p => p.CompleteAsync(It.IsAny<AiCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiCompletionRequest, CancellationToken>((r, _) => _sent = r)
            .ReturnsAsync(new AiCompletion("""{"risk":"low"}""", "configured-model-2026", 120, 15, 0.0012m));
        _meter.Setup(m => m.RecordAsync(It.IsAny<AiUsageRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AiUsageRecord, CancellationToken>((r, _) => _metered.Add(r)).Returns(Task.CompletedTask);
        // Stands in for variable interpolation: fills {{ amount }}; leaves anything else as it is.
        _interpolation.Setup(v => v.InterpolateAsync(_instanceId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string t, CancellationToken _) => t.Replace("{{ amount }}", "4200").Replace("{{ nric }}", "900101-14-5678"));
        _variables.Setup(v => v.GetForInstanceAsync(_instanceId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new WorkflowVariable { InstanceId = _instanceId, Name = "amount", Value = "4200" },
            new WorkflowVariable { InstanceId = _instanceId, Name = "nric", Value = "ciphertext", IsSensitive = true },
        ]);
    }

    private AiStepWorker Worker() => new(_provider.Object, _meter.Object, _interpolation.Object, _variables.Object, NullLogger<AiStepWorker>.Instance);

    private NodeExecutionContext Context(string configJson)
    {
        var node = new SfgNode("assess", NodeType.Ai, "Assess risk", JsonDocument.Parse(configJson), null, null, null);
        return new NodeExecutionContext(_instanceId, _workspaceId, node,
            new WorkflowGraph("w", "v1", [node], [], new WorkflowMetadata("w", null)), new Dictionary<string, JsonElement>(), "lease-1");
    }

    [Fact]
    public async Task Fills_the_prompt_calls_the_provider_and_meters_the_call()
    {
        var result = await Worker().ExecuteAsync(
            Context("""{ "prompt": "Assess a loan of {{ amount }}", "system_prompt": "Answer in JSON", "max_tokens": 300 }"""), default);

        result.Success.Should().BeTrue();
        result.Output!.RootElement.GetProperty("risk").GetString().Should().Be("low");
        _sent.Should().Be(new AiCompletionRequest(_workspaceId, "configured-model", "Answer in JSON", "Assess a loan of 4200", 300));
        _metered.Should().ContainSingle().Which.Should().Be(new AiUsageRecord(
            _workspaceId, _instanceId, "assess", "anthropic", "configured-model-2026", AiFunctions.NodeExec, 120, 15, 0.0012m, true));
    }

    [Fact]
    public async Task A_sensitive_variable_never_reaches_the_model()
    {
        await Worker().ExecuteAsync(Context("""{ "prompt": "Check {{ nric }}, {{nric.last4}} and {{ variables.nric }} for {{ amount }}" }"""), default);

        _sent!.Prompt.Should().Be("Check [SENSITIVE_REDACTED], [SENSITIVE_REDACTED] and [SENSITIVE_REDACTED] for 4200");
        _sent.Prompt.Should().NotContain("900101");
    }

    [Fact]
    public async Task The_step_model_overrides_the_default_and_a_reply_that_isnt_json_is_wrapped()
    {
        _provider.Setup(p => p.CompleteAsync(It.IsAny<AiCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiCompletionRequest, CancellationToken>((r, _) => _sent = r)
            .ReturnsAsync(new AiCompletion("Looks fine.", "step-model", 10, 3, null));

        var result = await Worker().ExecuteAsync(Context("""{ "prompt": "Hi", "model": "step-model" }"""), default);

        _sent!.Model.Should().Be("step-model");
        _sent.MaxTokens.Should().Be(1024);
        result.Output!.RootElement.GetProperty("text").GetString().Should().Be("Looks fine.");
        _metered.Single().CostUsd.Should().Be(0m, "no prices configured: cost 0, tokens still exact");
    }

    [Fact]
    public async Task No_model_anywhere_is_an_actionable_error_and_nothing_is_called()
    {
        _provider.SetupGet(p => p.DefaultModel).Returns((string?)null);

        var result = await Worker().ExecuteAsync(Context("""{ "prompt": "Hi" }"""), default);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Ai:Anthropic:Model");
        _provider.Verify(p => p.CompleteAsync(It.IsAny<AiCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_provider_failure_is_metered_and_retried_only_when_transient(bool retryable)
    {
        _provider.Setup(p => p.CompleteAsync(It.IsAny<AiCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiProviderException("Anthropic is busy (HTTP 529).", retryable));

        var result = await Worker().ExecuteAsync(Context("""{ "prompt": "Hi" }"""), default);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().Be(retryable);
        result.ErrorMessage.Should().Contain("HTTP 529");
        _metered.Should().ContainSingle().Which.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task If_usage_cant_be_recorded_the_step_fails_instead_of_running_unmetered()
    {
        _meter.Setup(m => m.RecordAsync(It.IsAny<AiUsageRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is down"));

        var result = await Worker().ExecuteAsync(Context("""{ "prompt": "Hi" }"""), default);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse("retrying would call the model again, unmetered");
        result.ErrorMessage.Should().Contain("couldn't record its AI usage");
    }

    [Fact]
    public async Task No_prompt_is_an_actionable_error()
    {
        var result = await Worker().ExecuteAsync(Context("""{ "model": "m" }"""), default);

        result.ErrorMessage.Should().Contain("has no prompt");
        _metered.Should().BeEmpty();
    }
}
