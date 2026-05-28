using FluentAssertions;
using FluentValidation;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
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
        WorkflowName: "Test Workflow",
        Purpose: "Test workflow for unit tests",
        TriggerDescription: "Manual trigger",
        StepsDescription: "Start the process\nDo some work\nFinish and notify the team",
        RulesAndConstraints: "None",
        SystemsAndAi: "None");

    private static readonly NlWorkflowRequest ComplexRequest = new(
        WorkflowName: "Complex Workflow",
        Purpose: "A complex process with approvals",
        TriggerDescription: "Webhook trigger",
        StepsDescription: "Step 1\nStep 2\nStep 3\nStep 4\nStep 5\nStep 6 with if condition\nStep 7 with approval\nStep 8 parallel",
        RulesAndConstraints: "Requires parallel approval flow",
        SystemsAndAi: "Slack, Email");

    [Fact]
    public async Task GenerateAsync_ValidInput_ReturnsValidYaml()
    {
        var (sut, aiMock, _, _, validatorMock, _) = BuildSut();
        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
            .ReturnsAsync(new AiCompletionResult(ValidYaml, 100, 200));
        validatorMock.Setup(x => x.ValidateAsync(ValidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        var result = await sut.GenerateAsync(TestRequest, WorkspaceId);

        result.Should().NotBeNull();
        result.ValidationResult.IsValid.Should().BeTrue();
        result.Cached.Should().BeFalse();
        result.TokensUsed.Should().Be(300);
        aiMock.Verify(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_InvalidYamlFromAi_RetriesWithErrorContext()
    {
        var (sut, aiMock, _, _, validatorMock, _) = BuildSut();
        var error = new ValidationIssue(2, "GRF-001", "Missing trigger", null, null, null);
        var callCount = 0;

        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
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
        aiMock.Verify(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GenerateAsync_SemanticCacheHit_NoAiCall()
    {
        var (sut, aiMock, cacheMock, _, validatorMock, _) = BuildSut();
        cacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidYaml);
        validatorMock.Setup(x => x.ValidateAsync(ValidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        var result = await sut.GenerateAsync(TestRequest, WorkspaceId);

        result.Cached.Should().BeTrue();
        result.TokensUsed.Should().Be(0);
        aiMock.Verify(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_StripsMarkdownFences_BeforeValidation()
    {
        var (sut, aiMock, _, _, validatorMock, _) = BuildSut();
        var fencedYaml = $"```yaml\n{ValidYaml}\n```";
        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
            .ReturnsAsync(new AiCompletionResult(fencedYaml, 100, 200));
        validatorMock.Setup(x => x.ValidateAsync(ValidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        var result = await sut.GenerateAsync(TestRequest, WorkspaceId);

        result.YamlContent.Should().NotStartWith("```");
        result.ValidationResult.IsValid.Should().BeTrue();
    }

    // FIX 4: Pre-screening
    [Fact]
    public async Task GenerateAsync_ShortStepsDescription_ThrowsValidationException()
    {
        var (sut, _, _, _, _, _) = BuildSut();
        var badRequest = TestRequest with { StepsDescription = "too short" };

        var act = async () => await sut.GenerateAsync(badRequest, WorkspaceId);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*too brief*");
    }

    [Fact]
    public async Task GenerateAsync_ShortWorkflowName_ThrowsValidationException()
    {
        var (sut, _, _, _, _, _) = BuildSut();
        var badRequest = TestRequest with { WorkflowName = "AB" };

        var act = async () => await sut.GenerateAsync(badRequest, WorkspaceId);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // FIX 5: Budget enforcement
    [Fact]
    public async Task GenerateAsync_BudgetExceeded_ThrowsPlanLimitException()
    {
        var (sut, _, _, _, _, budgetMock) = BuildSut();
        budgetMock.Setup(x => x.IsBudgetAvailableAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = async () => await sut.GenerateAsync(TestRequest, WorkspaceId);

        await act.Should().ThrowAsync<PlanLimitException>();
    }

    [Fact]
    public async Task GenerateAsync_CacheHit_SkipsBudgetCheck()
    {
        var (sut, aiMock, cacheMock, _, validatorMock, budgetMock) = BuildSut();
        cacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidYaml);
        budgetMock.Setup(x => x.IsBudgetAvailableAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        validatorMock.Setup(x => x.ValidateAsync(ValidYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        // Should NOT throw even though budget is exhausted — cache hits bypass the AI cost
        var result = await sut.GenerateAsync(TestRequest, WorkspaceId);

        result.Cached.Should().BeTrue();
        budgetMock.Verify(x => x.IsBudgetAvailableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // FIX 3: Complexity routing
    [Theory]
    [InlineData("simple step one line", 1)]     // 1 line, no keywords → score 1 → Haiku
    [InlineData("step\nstep\nstep", 3)]          // 3 lines, no keywords → score 3 → Haiku
    public void ComplexityScore_SimpleWorkflow_ScoreBelowThreshold(string steps, int expectedScore)
    {
        var request = TestRequest with { StepsDescription = steps };
        NlYamlGenerationService.ComplexityScore(request).Should().Be(expectedScore);
    }

    [Fact]
    public void ComplexityScore_ApprovalKeyword_AddsPoints()
    {
        var request = TestRequest with { StepsDescription = "Step with approval" };
        NlYamlGenerationService.ComplexityScore(request).Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task GenerateAsync_SimpleWorkflow_UsesHaikuModel()
    {
        var (sut, aiMock, _, _, validatorMock, _) = BuildSut();
        ModelConfig? capturedConfig = null;
        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
            .Callback<ModelConfig, string, string, CancellationToken, int>((c, _, _, _, _) => capturedConfig = c)
            .ReturnsAsync(new AiCompletionResult(ValidYaml, 50, 100));
        validatorMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        // TestRequest has "Do one thing" as StepsDescription — complexity score = 1 → Haiku
        await sut.GenerateAsync(TestRequest, WorkspaceId);

        capturedConfig!.ModelId.Should().Be("claude-haiku-4-5");
    }

    [Fact]
    public async Task GenerateAsync_ComplexWorkflow_UsesSonnetModel()
    {
        var (sut, aiMock, _, _, validatorMock, _) = BuildSut();
        ModelConfig? capturedConfig = null;
        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
            .Callback<ModelConfig, string, string, CancellationToken, int>((c, _, _, _, _) => capturedConfig = c)
            .ReturnsAsync(new AiCompletionResult(ValidYaml, 100, 200));
        validatorMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(true, [], [], []));

        await sut.GenerateAsync(ComplexRequest, WorkspaceId);

        capturedConfig!.ModelId.Should().Be("claude-sonnet-4-6"); // TestModel is Sonnet; complex → keep Sonnet
    }

    private static (NlYamlGenerationService sut,
        Mock<IAiCompletionService> ai,
        Mock<ISemanticCacheService> cache,
        Mock<IAiTokenMeteringService> metering,
        Mock<IWorkflowValidator> validator,
        Mock<IAiBudgetService> budget) BuildSut()
    {
        var ai = new Mock<IAiCompletionService>();
        var models = new Mock<IModelResolutionService>();
        var cache = new Mock<ISemanticCacheService>();
        var metering = new Mock<IAiTokenMeteringService>();
        var validator = new Mock<IWorkflowValidator>();
        var budget = new Mock<IAiBudgetService>();

        models.Setup(x => x.ResolveModelConfigAsync(AiFunctionIds.NlYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestModel);
        cache.Setup(x => x.ComputeKey(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns("test-cache-key");
        cache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.Setup(x => x.IsBudgetAvailableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new NlYamlGenerationService(ai.Object, models.Object, cache.Object, metering.Object, validator.Object,
            budget.Object, NullLogger<NlYamlGenerationService>.Instance);

        return (sut, ai, cache, metering, validator, budget);
    }
}
