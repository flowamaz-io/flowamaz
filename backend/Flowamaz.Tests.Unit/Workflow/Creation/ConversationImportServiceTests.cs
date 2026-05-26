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

public sealed class ConversationImportServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly ModelConfig TestModel = new("claude-sonnet-4-6", "anthropic",
        AiKeySource.Platform, "key", false, 200000, true, true);

    private const string SlackThread = """
        [{"user":"anita","text":"Can someone approve this $8000 purchase request?"},
         {"user":"rajan","text":"I can approve up to $5000, need CFO for above that"},
         {"user":"cfo","text":"I'll review the SAP PO before approving"}]
        """;

    private const string ExtractionJson = """
        {
          "workflow_name": "Purchase Approval",
          "summary": "Routes purchase requests to manager or CFO based on amount",
          "steps": ["Submit request", "Route by amount", "Manager or CFO approval", "Create SAP PO"],
          "approvers": ["Rajan (up to $5000)", "CFO (above $5000)"],
          "systems": ["SAP"],
          "trigger": "Purchase request submitted",
          "rules": "Amounts above $5000 require CFO approval",
          "confidence": 0.92
        }
        """;

    private const string ValidYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: purchase-approval
          name: "Purchase Approval"
          version: "1.0.0"
        spec:
          trigger:
            type: webhook
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

    [Fact]
    public async Task ImportAsync_SlackJson_ParsedAndExtracted()
    {
        var (sut, aiMock, generatorMock) = BuildSut();
        SetupAi(aiMock, ExtractionJson);
        SetupGenerator(generatorMock, ValidYaml);

        var result = await sut.ImportAsync(SlackThread, "slack", WorkspaceId);

        result.Should().NotBeNull();
        result.YamlContent.Should().Contain("purchase-approval");
        result.ConfidenceScore.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task ImportAsync_ConversationWithApprover_ApproverInExtractedProcess()
    {
        var (sut, aiMock, generatorMock) = BuildSut();
        SetupAi(aiMock, ExtractionJson);
        SetupGenerator(generatorMock, ValidYaml);

        var result = await sut.ImportAsync(SlackThread, "slack", WorkspaceId);

        result.ExtractedProcess.Approvers.Should().Contain(a => a.Contains("Rajan") || a.Contains("CFO"));
    }

    [Fact]
    public async Task ImportAsync_GenericText_Works()
    {
        var (sut, aiMock, generatorMock) = BuildSut();
        SetupAi(aiMock, ExtractionJson);
        SetupGenerator(generatorMock, ValidYaml);

        var result = await sut.ImportAsync("Manager approves purchases over $5000", "generic", WorkspaceId);

        result.Should().NotBeNull();
        result.YamlContent.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ImportAsync_MalformedSlackJson_FallsBackToRawText()
    {
        var (sut, aiMock, generatorMock) = BuildSut();
        SetupAi(aiMock, ExtractionJson);
        SetupGenerator(generatorMock, ValidYaml);

        // Not valid JSON but treated as slack source — should not throw
        var result = await sut.ImportAsync("anita: Can someone approve this?", "slack", WorkspaceId);

        result.Should().NotBeNull();
    }

    private static void SetupAi(Mock<IAiCompletionService> aiMock, string responseText)
    {
        aiMock.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(responseText, 300, 200));
    }

    private static void SetupGenerator(Mock<INlYamlGenerationService> generatorMock, string yaml)
    {
        generatorMock.Setup(x => x.GenerateAsync(It.IsAny<NlWorkflowRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationResult(yaml, new ValidationResult(true, [], [], []), 400, false));
    }

    private static (ConversationImportService sut, Mock<IAiCompletionService> ai, Mock<INlYamlGenerationService> generator) BuildSut()
    {
        var ai = new Mock<IAiCompletionService>();
        var models = new Mock<IModelResolutionService>();
        var metering = new Mock<IAiTokenMeteringService>();
        var generator = new Mock<INlYamlGenerationService>();

        models.Setup(x => x.ResolveModelConfigAsync(AiFunctionIds.NlYaml, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestModel);

        var sut = new ConversationImportService(ai.Object, models.Object, metering.Object, generator.Object,
            NullLogger<ConversationImportService>.Instance);
        return (sut, ai, generator);
    }
}
