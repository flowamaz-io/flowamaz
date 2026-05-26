using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

public sealed class NlYamlGenerationServiceTests
{
    private const string ValidYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: test-workflow
          name: "Test Workflow"
          version: "1.0.0"
        spec:
          trigger:
            type: manual
          nodes:
            - id: start
              type: trigger
              label: "Start"
              config: {}
            - id: end
              type: end
              label: "End"
              config: {}
          edges:
            - id: e1
              from: start
              to: end
        """;

    private const string InvalidYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: broken
        spec:
          trigger:
            type: manual
          nodes: []
          edges: []
        """;

    private static readonly ModelConfig TestModel = new("claude-sonnet-4-6", "anthropic",
        AiKeySource.Platform, "test-key", false, 200000, true, true);

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private static readonly NlWorkflowRequest TestRequest = new(
        WorkflowName: "Test",
        Purpose: "Test workflow",
        TriggerDescription: "Manual trigger",
        StepsDescription: "Do one thing",
        RulesAndConstraints: "None",
        SystemsAndAi: "None");

    [Fact]
    public async Task GenerateAsync_ValidInput_ReturnsValidYaml()
    {
        var (sut, aiMock, _, _, validatorMock) = BuildSut();
        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(ValidYaml, 100, 200));
        validatorMock.Setup(x => x.ValidateAsync(ValidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        var result = await sut.GenerateAsync(TestRequest, WorkspaceId);

        result.Should().NotBeNull();
        result.ValidationResult.IsValid.Should().BeTrue();
        result.Cached.Should().BeFalse();
        result.TokensUsed.Should().Be(300);
        aiMock.Verify(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_InvalidYamlFromAi_RetriesWithErrorContext()
    {
        var (sut, aiMock, _, _, validatorMock) = BuildSut();
        var error = new ValidationIssue(2, "GRF-001", "Missing trigger", null, null, null);
        var callCount = 0;

        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new AiCompletionResult(callCount == 1 ? InvalidYaml : ValidYaml, 100, 200);
            });

        validatorMock.Setup(x => x.ValidateAsync(InvalidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(false, [error], [], []));
        validatorMock.Setup(x => x.ValidateAsync(ValidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        var result = await sut.GenerateAsync(TestRequest, WorkspaceId);

        result.ValidationResult.IsValid.Should().BeTrue();
        aiMock.Verify(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GenerateAsync_SemanticCacheHit_NoAiCall()
    {
        var (sut, aiMock, cacheMock, _, validatorMock) = BuildSut();
        cacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidYaml);
        validatorMock.Setup(x => x.ValidateAsync(ValidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        var result = await sut.GenerateAsync(TestRequest, WorkspaceId);

        result.Cached.Should().BeTrue();
        result.TokensUsed.Should().Be(0);
        aiMock.Verify(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_StripsMarkdownFences_BeforeValidation()
    {
        var (sut, aiMock, _, _, validatorMock) = BuildSut();
        var fencedYaml = $"```yaml\n{ValidYaml}\n```";
        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(fencedYaml, 100, 200));
        validatorMock.Setup(x => x.ValidateAsync(ValidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        var result = await sut.GenerateAsync(TestRequest, WorkspaceId);

        result.YamlContent.Should().NotStartWith("```");
        result.ValidationResult.IsValid.Should().BeTrue();
    }

    private static (NlYamlGenerationService sut,
        Mock<IAiCompletionService> ai,
        Mock<ISemanticCacheService> cache,
        Mock<IAiTokenMeteringService> metering,
        Mock<IWorkflowValidator> validator) BuildSut()
    {
        var ai = new Mock<IAiCompletionService>();
        var models = new Mock<IModelResolutionService>();
        var cache = new Mock<ISemanticCacheService>();
        var metering = new Mock<IAiTokenMeteringService>();
        var validator = new Mock<IWorkflowValidator>();

        models.Setup(x => x.ResolveModelConfigAsync(AiFunctionIds.NlYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestModel);
        cache.Setup(x => x.ComputeKey(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns("test-cache-key");
        cache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new NlYamlGenerationService(ai.Object, models.Object, cache.Object, metering.Object, validator.Object,
            NullLogger<NlYamlGenerationService>.Instance);

        return (sut, ai, cache, metering, validator);
    }
}
