using System.Text;
using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

public sealed class SopParsingServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private static ModelConfig TestConfig() =>
        new("claude-sonnet-4-6", "anthropic", AiKeySource.Platform, "key", false, 200_000, true, true);

    private static Mock<IModelResolutionService> Resolver()
    {
        var m = new Mock<IModelResolutionService>();
        m.Setup(r => r.ResolveModelConfigAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestConfig());
        return m;
    }

    private static Mock<IAiCompletionService> AiReturnsExtractionJson(string json)
    {
        var m = new Mock<IAiCompletionService>();
        m.Setup(a => a.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(json, 200, 80));
        return m;
    }

    private static Mock<INlYamlGenerationService> GeneratorReturns(string yaml)
    {
        var m = new Mock<INlYamlGenerationService>();
        m.Setup(g => g.GenerateAsync(It.IsAny<NlWorkflowRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationResult(yaml, Core.Workflow.ValidationResult.Valid(), 150, false));
        return m;
    }

    private SopParsingService Build(
        Mock<IAiCompletionService>? ai = null,
        Mock<INlYamlGenerationService>? generator = null,
        Mock<IAiTokenMeteringService>? metering = null)
    {
        var defaultJson = """{"workflow_name":"Test","purpose":"Test","trigger":"Manual","steps":["Step 1","Step 2"],"rules":"","systems":""}""";
        ai ??= AiReturnsExtractionJson(defaultJson);
        generator ??= GeneratorReturns("workflow: test");
        metering ??= new Mock<IAiTokenMeteringService>();

        return new SopParsingService(
            Resolver().Object, ai.Object, metering.Object, generator.Object,
            NullLogger<SopParsingService>.Instance);
    }

    [Fact]
    public async Task PlainText_Parses_And_Meters()
    {
        var metering = new Mock<IAiTokenMeteringService>();
        var svc = Build(metering: metering);
        var bytes = Encoding.UTF8.GetBytes("Step 1: Do something\nStep 2: Approve it");

        var result = await svc.ParseAsync(bytes, "text/plain", WorkspaceId);

        result.YamlContent.Should().Be("workflow: test");
        result.PageCount.Should().Be(1);
        result.WordCount.Should().BeGreaterThan(0);
        metering.Verify(m => m.RecordUsage(
            AiFunctionIds.DocParse, It.IsAny<string>(), It.IsAny<string>(), null, WorkspaceId,
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Once);
    }

    [Fact]
    public async Task Extraction_Json_With_Fence_Is_Cleaned()
    {
        var json = "```json\n{\"workflow_name\":\"Invoice\",\"purpose\":\"Approval\",\"trigger\":\"Manual\",\"steps\":[\"Review\",\"Approve\"],\"rules\":\"\",\"systems\":\"\"}\n```";
        var generator = new Mock<INlYamlGenerationService>();
        NlWorkflowRequest? captured = null;
        generator.Setup(g => g.GenerateAsync(It.IsAny<NlWorkflowRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<NlWorkflowRequest, Guid, CancellationToken>((r, _, _) => captured = r)
            .ReturnsAsync(new GenerationResult("yaml: ok", Core.Workflow.ValidationResult.Valid(), 100, false));

        var svc = Build(ai: AiReturnsExtractionJson(json), generator: generator);
        var bytes = Encoding.UTF8.GetBytes("some document text");

        await svc.ParseAsync(bytes, "text/plain", WorkspaceId);

        captured!.WorkflowName.Should().Be("Invoice");
        captured.StepsDescription.Should().Contain("Review");
    }

    [Fact]
    public async Task Broken_Json_Falls_Back_To_Default()
    {
        var svc = Build(ai: AiReturnsExtractionJson("not json at all {{{"));

        var result = await svc.ParseAsync(Encoding.UTF8.GetBytes("test"), "text/plain", WorkspaceId);

        result.Should().NotBeNull();
        result.YamlContent.Should().Be("workflow: test");
    }

    [Fact]
    public async Task ExtractedSteps_Reflect_Json_Steps()
    {
        var json = """{"workflow_name":"Onboard","purpose":"Hire","trigger":"Form","steps":["Send offer","Setup laptop","Create accounts"],"rules":"","systems":""}""";
        var svc = Build(ai: AiReturnsExtractionJson(json));
        var bytes = Encoding.UTF8.GetBytes("HR onboarding SOP");

        var result = await svc.ParseAsync(bytes, "text/plain", WorkspaceId);

        result.ExtractedSteps.Should().HaveCount(3);
        result.ExtractedSteps[0].Should().Be("Send offer");
    }
}
