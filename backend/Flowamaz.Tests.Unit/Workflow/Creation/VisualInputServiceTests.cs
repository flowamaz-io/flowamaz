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

public sealed class VisualInputServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly ModelConfig TestModel = new("claude-sonnet-4-6", "anthropic",
        AiKeySource.Platform, "key", true, 200000, true, true);

    private const string ValidParseJson = """
        {
          "nodes": [
            {"id":"n1","label":"Submit Request","type":"trigger","x":100,"y":100,"confidence":0.98,"annotations":["Typeform"]},
            {"id":"n2","label":"Amount > $5000?","type":"router","x":100,"y":200,"confidence":0.94,"annotations":[]},
            {"id":"n3","label":"Manager Approval","type":"human-gate","x":100,"y":300,"confidence":0.91,"annotations":[]},
            {"id":"n4","label":"Low Conf Step","type":"action","x":200,"y":300,"confidence":0.70,"annotations":["SAP"]},
            {"id":"n5","label":"End","type":"end","x":100,"y":400,"confidence":0.99,"annotations":[]}
          ],
          "edges": [
            {"from":"n1","to":"n2","label":null},
            {"from":"n2","to":"n3","label":"amount > 5000"},
            {"from":"n3","to":"n5","label":null}
          ],
          "low_confidence": ["n4"]
        }
        """;

    [Fact]
    public async Task ProcessImageAsync_ValidResponse_ExtractsHighConfidenceNodes()
    {
        var (sut, aiMock, _, _) = BuildSut();
        aiMock.Setup(x => x.CompleteWithImageAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(ValidParseJson, 500, 300));

        var result = await sut.ProcessImageAsync([1, 2, 3], "image/jpeg", WorkspaceId);

        result.Should().NotBeNull();
        result.YamlDraft.Should().Contain("apiVersion: flowamaz/v1");
        result.YamlDraft.Should().Contain("n1");
        result.YamlDraft.Should().NotContain("n4"); // low confidence excluded
    }

    [Fact]
    public async Task ProcessImageAsync_LowConfidenceNode_AppearsInLowConfidenceList()
    {
        var (sut, aiMock, _, _) = BuildSut();
        aiMock.Setup(x => x.CompleteWithImageAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(ValidParseJson, 500, 300));

        var result = await sut.ProcessImageAsync([1, 2, 3], "image/jpeg", WorkspaceId);

        result.LowConfidenceElements.Should().HaveCount(1);
        result.LowConfidenceElements[0].Id.Should().Be("n4");
        result.LowConfidenceElements[0].Confidence.Should().BeLessThan(0.80);
    }

    [Fact]
    public async Task ProcessImageAsync_SapAnnotation_MappedToConnectorId()
    {
        var sapNodeJson = """
            {"nodes":[{"id":"n1","label":"SAP Step","type":"action","x":100,"y":100,"confidence":0.95,"annotations":["SAP","ERP"]}],
             "edges":[],"low_confidence":[]}
            """;
        var (sut, aiMock, _, _) = BuildSut();
        aiMock.Setup(x => x.CompleteWithImageAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(sapNodeJson, 200, 100));

        var result = await sut.ProcessImageAsync([1], "image/jpeg", WorkspaceId);

        result.YamlDraft.Should().Contain("sap-s4hana");
    }

    [Fact]
    public async Task ProcessImageAsync_MalformedJson_ReturnsEmptyYaml()
    {
        var (sut, aiMock, _, _) = BuildSut();
        aiMock.Setup(x => x.CompleteWithImageAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult("not valid json at all {{{", 100, 50));

        var result = await sut.ProcessImageAsync([1], "image/jpeg", WorkspaceId);

        result.YamlDraft.Should().BeEmpty();
        result.LowConfidenceElements.Should().BeEmpty();
    }

    private static (VisualInputService sut, Mock<IAiCompletionService> ai, Mock<IAiTokenMeteringService> metering, Mock<IWorkflowValidator> validator) BuildSut()
    {
        var ai = new Mock<IAiCompletionService>();
        var models = new Mock<IModelResolutionService>();
        var metering = new Mock<IAiTokenMeteringService>();
        var validator = new Mock<IWorkflowValidator>();

        models.Setup(x => x.ResolveModelConfigAsync(AiFunctionIds.VisualInput, WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestModel);

        var sut = new VisualInputService(ai.Object, models.Object, metering.Object, validator.Object,
            NullLogger<VisualInputService>.Instance);
        return (sut, ai, metering, validator);
    }
}
